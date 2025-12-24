using AeroGL.Data;
using Dapper;
using System;
using System.Globalization;
using System.Linq;
using System.Printing;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace AeroGL
{
    public partial class ReportNeracaWindow : Window
    {
        public string CompanyName { get; set; } = "Nama PT";

        public ReportNeracaWindow()
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

        // ===== Raw model dari DB =====
        private sealed class BalRow
        {
            public string Code3 { get; set; }   // e.g. 001.123.001
            public string Name { get; set; }
            public string Type { get; set; }    // "D" / "K" (dinormalisasi di SQL)
            public double Saldo { get; set; }
            public double Debet { get; set; }
            public double Kredit { get; set; }
        }

        // ===== View model untuk binding =====
        public sealed class NeracaView
        {
            public string Title { get; set; }
            public string Period { get; set; }

            // Aktiva Lancar
            public decimal Kas { get; set; }
            public decimal Bank { get; set; }
            public decimal PiutangDagang { get; set; }
            public decimal Persediaan { get; set; }
            public decimal PiutangKaryawan { get; set; }
            public decimal PajakDibayarDimuka { get; set; }
            public decimal JumlahAktivaLancar => Kas + Bank + PiutangDagang + Persediaan + PiutangKaryawan + PajakDibayarDimuka;

            // Aktiva Tetap
            public decimal AktivaTetap { get; set; }
            public decimal AkumulasiPenyusutan { get; set; }
            public decimal AkumulasiPenyusutanNeg => -AkumulasiPenyusutan; // tampil minus (kurung)
            public decimal JumlahAktivaTetap => AktivaTetap - AkumulasiPenyusutan;

            public decimal TotalAktiva => JumlahAktivaLancar + JumlahAktivaTetap;

            // Passiva Lancar
            public decimal HutangDagang { get; set; }
            public decimal HutangBank { get; set; }
            public decimal HutangLain { get; set; }
            public decimal BYMHDibayar { get; set; }   // pengurang
            public decimal BYMHDibayarNeg => -BYMHDibayar;
            public decimal PYMHDibayar { get; set; }
            public decimal JumlahPassivaLancar => HutangDagang + HutangBank + HutangLain - BYMHDibayar + PYMHDibayar;

            // Modal (ambil semua anak 015/016/017)
            public decimal ModalDisetor { get; set; }
            public decimal LabaDitahan { get; set; }
            public decimal LabaBerjalan { get; set; }
            public decimal JumlahModal => ModalDisetor + LabaDitahan + LabaBerjalan;

            public decimal TotalPassiva => JumlahPassivaLancar + JumlahModal;
        }

        // ===== Helpers =====
        private static string FormatPeriod(int year, int month)
        {
            var id = CultureInfo.GetCultureInfo("id-ID");
            return new DateTime(year, month, 1).ToString("MMMM yyyy", id);
        }

        private static string FirstSeg(string code3)
        {
            if (string.IsNullOrEmpty(code3)) return "";
            int dot = code3.IndexOf('.');
            return dot > 0 ? code3.Substring(0, dot) : code3;
        }

        private static decimal Closing(BalRow r)
        {
            var s = (decimal)r.Saldo;
            var d = (decimal)r.Debet;
            var k = (decimal)r.Kredit;
            return r.Type == "D" ? (s + d - k) : (s + k - d);
        }

        private static decimal SumByFirst(BalRow[] rows, string firstSeg)
        {
            return rows.Where(r => FirstSeg(r.Code3) == firstSeg)
                       .Sum(Closing);
        }

        // ===== Core builder (BULAN SAJA) =====
        private async Task<NeracaView> BuildView(int year, int month)
        {
            // Normalisasi nilai (REAL/TEXT dgn koma/titik) + paksa tipe akun jadi "D"/"K"
            string norm(string col) =>
                $"(CASE WHEN typeof({col})='text' THEN 1.0 * CAST(REPLACE({col}, ',', '.') AS REAL) ELSE 1.0 * {col} END)";
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

            BalRow[] raw;
            using (var cn = Db.Open())
            {
                raw = (await cn.QueryAsync<BalRow>(sqlMonth, new { y = year, m = month }))
                       .ToArray();
            }

            // ===== Hitung per kelompok (semua pakai SumByFirst: xxx.*.*) =====
            // AKTIVA LANCAR
            var kas = SumByFirst(raw, "001");
            var bank = SumByFirst(raw, "002");
            var piutangDagang = SumByFirst(raw, "003");
            var persediaan = SumByFirst(raw, "004");
            var piutangKaryawan = SumByFirst(raw, "005");
            var pajakDimuka = SumByFirst(raw, "006");

            // AKTIVA TETAP
            var aktivaTetap = SumByFirst(raw, "007");
            var akumPenyusutan = Math.Abs(SumByFirst(raw, "008"));   // pengurang (ditampilkan dalam kurung)

            // PASSIVA LANCAR
            var hutangDagang = SumByFirst(raw, "010");
            var hutangBank = SumByFirst(raw, "011");
            var hutangLain = SumByFirst(raw, "012");
            var bymhDibayar = Math.Abs(SumByFirst(raw, "013"));      // pengurang subtotal
            var pymhDibayar = SumByFirst(raw, "014");

            // MODAL (semua anak)
            var modalDisetor = SumByFirst(raw, "015");
            string pDitahan = FirstSeg(AccountConfig.PrefixLabaDitahan);
            string pBerjalan = FirstSeg(AccountConfig.PrefixLabaBerjalan);

            var labaDitahan = SumByFirst(raw, pDitahan);
            var labaBerjalan = SumByFirst(raw, pBerjalan);

            var vm = new NeracaView
            {
                Title = $"LAPORAN NERACA — {CompanyName}",
                Period = FormatPeriod(year, month),

                Kas = kas,
                Bank = bank,
                PiutangDagang = piutangDagang,
                Persediaan = persediaan,
                PiutangKaryawan = piutangKaryawan,
                PajakDibayarDimuka = pajakDimuka,

                AktivaTetap = aktivaTetap,
                AkumulasiPenyusutan = akumPenyusutan,

                HutangDagang = hutangDagang,
                HutangBank = hutangBank,
                HutangLain = hutangLain,
                BYMHDibayar = bymhDibayar,
                PYMHDibayar = pymhDibayar,

                ModalDisetor = modalDisetor,
                LabaDitahan = labaDitahan,
                LabaBerjalan = labaBerjalan
            };

            return vm;
        }

        // ===== UI handlers =====
        private async void BtnShow_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                int year = int.TryParse(TxtYear.Text, out var y) ? y : DateTime.Today.Year;
                int month = CboMonth.SelectedIndex + 1;

                Mouse.OverrideCursor = Cursors.Wait;
                TxtInfo.Text = "Menghitung…";

                var vm = await BuildView(year, month);
                DataContext = vm;

                // Info baris bawah: tampilkan periode + warning kalau selisih
                if (Math.Round(vm.TotalAktiva - vm.TotalPassiva, 2) != 0m)
                    TxtInfo.Text = $"{vm.Period} • WARNING: selisih {Math.Abs(vm.TotalAktiva - vm.TotalPassiva):N2}";
                else
                    TxtInfo.Text = vm.Period;
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
            if (dlg.ShowDialog() != true) return;

            dlg.PrintTicket.PageOrientation = PageOrientation.Portrait;

            // Terapkan tema cetak (hitam + margin kanan) dan atur width agar muat
            PrintThemeState st = null;
            try
            {
                st = ApplyPrintTheme(Paper, dlg.PrintableAreaWidth);

                // Optional: kalau mau benar-benar aman dari kepotong bawah,
                // tambah scale-to-fit height juga (kalau tinggi visual > area cetak):
                Paper.UpdateLayout();
                var original = Paper.LayoutTransform;

                double printableW = dlg.PrintableAreaWidth;
                double printableH = dlg.PrintableAreaHeight;

                double w = Paper.ActualWidth > 0 ? Paper.ActualWidth : Paper.DesiredSize.Width;
                double h = Paper.ActualHeight > 0 ? Paper.ActualHeight : Paper.DesiredSize.Height;

                // skala hanya jika perlu (tinggi melebihi area)
                double scale = Math.Min(1.0, printableH / h);
                Paper.LayoutTransform = new ScaleTransform(scale, scale);
                Paper.Measure(new Size(printableW, printableH));
                Paper.Arrange(new Rect(new Point(0, 0), new Size(printableW, printableH)));
                Paper.UpdateLayout();

                dlg.PrintVisual(Paper, "Neraca");

                // pulihkan transform
                Paper.LayoutTransform = original;
                Paper.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                Paper.Arrange(new Rect(Paper.DesiredSize));
                Paper.UpdateLayout();
            }
            finally
            {
                RestorePrintTheme(Paper, st);
            }
        }

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
    }
}
