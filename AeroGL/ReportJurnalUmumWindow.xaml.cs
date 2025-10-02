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
    public partial class ReportJurnalUmumWindow : Window
    {
        private readonly ICoaRepository _coaRepo = new CoaRepository();
        private readonly Dictionary<string, string> _nameCache = new Dictionary<string, string>();
        public string CompanyName { get; set; }

        public ReportJurnalUmumWindow()
        {
            InitializeComponent();
            this.UseLayoutRounding = true;
            this.SnapsToDevicePixels = true;
            DpFrom.SelectedDate = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
            DpTo.SelectedDate = DateTime.Today;

            // hotkeys
            // hotkeys
            PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.Escape) { Close(); e.Handled = true; }
                else if (e.Key == Key.Enter) { BtnShow_Click(s, e); e.Handled = true; }
                else if (e.Key == Key.P && Keyboard.Modifiers == ModifierKeys.None)
                {
                    BtnPrint_Click(s, e);
                    e.Handled = true;
                }
            };

        }
        private void BtnPrint_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var items = GridRows.ItemsSource as IEnumerable<JurnalUmumRow>;
                if (items == null || !items.Any())
                {
                    MessageBox.Show("Tidak ada data untuk dicetak. Tampilkan data dulu.",
                        "Print", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var from = DpFrom.SelectedDate ?? DateTime.Today;
                var to = DpTo.SelectedDate ?? DateTime.Today;

                var dlg = new PrintDialog();
                if (dlg.ShowDialog() == true)
                {
                    // (opsional) paksa landscape supaya lega
                    if (dlg.PrintTicket != null)
                        dlg.PrintTicket.PageOrientation = System.Printing.PageOrientation.Landscape;

                    // hitung lebar yang benar-benar bisa dipakai (px @ 96 DPI)
                    double pagePadding = 36; // kiri/kanan sama
                    double availWidth = dlg.PrintableAreaWidth - (pagePadding * 2);

                    // bangun dokumen dengan lebar tersedia
                    var doc = BuildFlowDocument(items, from, to, availWidth, pagePadding);

                    // set ukuran halaman agar sesuai printer
                    doc.PageWidth = dlg.PrintableAreaWidth;
                    doc.PageHeight = dlg.PrintableAreaHeight;

                    IDocumentPaginatorSource idp = doc;
                    dlg.PrintDocument(idp.DocumentPaginator, "Jurnal Umum");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Gagal mencetak", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }


        private async void BtnShow_Click(object sender, RoutedEventArgs e)
        {
            var d1 = DpFrom.SelectedDate ?? DateTime.Today;
            var d2 = DpTo.SelectedDate ?? DateTime.Today;
            if (d2 < d1)
            {
                MessageBox.Show("Tanggal sampai harus >= tanggal dari.", "Validasi",
                    MessageBoxButton.OK, MessageBoxImage.Exclamation);
                return;
            }

            try
            {
                Mouse.OverrideCursor = Cursors.Wait;
                GridRows.ItemsSource = null;

                var rows = await LoadReportRows(d1, d2);
                GridRows.ItemsSource = rows;
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Gagal memuat laporan", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                Mouse.OverrideCursor = null;
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();
        private static string FormatPeriod(DateTime d1, DateTime d2)
        {
            var id = CultureInfo.GetCultureInfo("id-ID");
            if (d1.Year == d2.Year && d1.Month == d2.Month)
                return d1.ToString("MMMM yyyy", id);

            if (d1.Year == d2.Year)
                return $"{d1.ToString("MMMM", id)} – {d2.ToString("MMMM yyyy", id)}";

            return $"{d1.ToString("MMMM yyyy", id)} – {d2.ToString("MMMM yyyy", id)}";
        }

        // ========= Data loader =========
        private async Task<List<JurnalUmumRow>> LoadReportRows(DateTime from, DateTime to)
        {
            var fromIso = from.ToString("yyyy-MM-dd");
            var toIso = to.ToString("yyyy-MM-dd");

            // Ambil join header+line dalam range (urut tanggal, notran, urutan baris)
            List<JoinRow> raw;
            using (var cn = Db.Open())
            {
                raw = (await cn.QueryAsync<JoinRow>(@"
SELECT  h.NoTran, h.Tanggal, h.Type,
        l.Code2, l.Side, l.Amount, l.Narration,
        l.rowid AS LineId
FROM JournalHeader h
JOIN JournalLine   l ON l.NoTran = h.NoTran
WHERE date(h.Tanggal) BETWEEN date(@d1) AND date(@d2)
ORDER BY h.Tanggal, h.NoTran, l.rowid;", new { d1 = fromIso, d2 = toIso }))
                    .AsList();
            }

            var result = new List<JurnalUmumRow>(raw.Count + 8);

            string prevKey = null;
            int no = 0;

            foreach (var r in raw)
            {
                var key = r.NoTran;
                var isFirstOfHeader = key != prevKey;
                if (isFirstOfHeader) { no++; prevKey = key; }

                // resolve nama akun (cache)
                var name = await ResolveAccountNameByCode2(r.Code2);

                // map baris tampil
                var id = CultureInfo.GetCultureInfo("id-ID");
                var row = new JurnalUmumRow
                {
                    // Header-only fields (tampilkan hanya di baris pertama dari suatu header)
                    NoTampil = isFirstOfHeader ? no.ToString() : "",
                    TanggalTampil = isFirstOfHeader ? ToDmy(r.Tanggal) : "",
                    NoTranTampil = isFirstOfHeader ? r.NoTran : "",

                    // Keterangan: "xxx.xxx — NAMA"
                    Ket = $"{r.Code2} — {name}",

                    DebetStr = r.Side == "D" ? r.Amount.ToString("N2", id) : null,
                    KreditStr = r.Side == "K" ? r.Amount.ToString("N2", id) : null,

                    Type = (r.Type == "M") ? "M" : "J"
                };

                result.Add(row);
            }

            return result;
        }

        private string ToDmy(string iso)
        {
            // iso yyyy-MM-dd -> dd-MM-yy
            if (DateTime.TryParse(iso, out var dt)) return dt.ToString("dd-MM-yy");
            return iso;
        }

        private async Task<string> ResolveAccountNameByCode2(string code2)
        {
            if (string.IsNullOrWhiteSpace(code2)) return "";
            if (_nameCache.TryGetValue(code2, out var nm)) return nm;

            var coa = await _coaRepo.Get(code2 + ".001");
            nm = coa?.Name ?? "";
            _nameCache[code2] = nm;
            return nm;
        }

        // ========= DTOs =========
        private sealed class JoinRow
        {
            public string NoTran { get; set; }
            public string Tanggal { get; set; } // ISO yyyy-MM-dd
            public string Type { get; set; }    // J/M
            public string Code2 { get; set; }
            public string Side { get; set; }    // D/K
            public decimal Amount { get; set; }
            public string Narration { get; set; }
            public long LineId { get; set; }
        }

        public sealed class JurnalUmumRow
        {
            public string NoTampil { get; set; }
            public string TanggalTampil { get; set; }
            public string NoTranTampil { get; set; }
            public string Ket { get; set; }
            public string DebetStr { get; set; }
            public string KreditStr { get; set; }
            public string Type { get; set; }
        }
        private FlowDocument BuildFlowDocument(
    IEnumerable<JurnalUmumRow> rows, DateTime d1, DateTime d2,
    double availableWidth, double pagePadding)
        {
            var doc = new FlowDocument
            {
                FontFamily = new FontFamily("Consolas"),
                FontSize = 12,
                PagePadding = new Thickness(pagePadding),
                ColumnWidth = double.PositiveInfinity
            };

            // --- HEADER (tetap sama seperti punyamu) ---
            string nama = string.IsNullOrWhiteSpace(CompanyName) ? "Nama PT" : CompanyName;
            doc.Blocks.Add(new Paragraph(new Run(nama))
            {
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 0, 0, 0)
            });

            doc.Blocks.Add(new Paragraph(new Run("LAPORAN JURNAL UMUM"))
            {
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 0, 0, 2)
            });

            doc.Blocks.Add(new Paragraph(new Run(FormatPeriod(d1, d2)))
            {
                FontSize = 12,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 0, 0, 12)
            });
            var table = new Table { CellSpacing = 0 };
            doc.Blocks.Add(table);

            // Lebar fixed kolom non-Keterangan
            double wNo = 40;
            double wTgl = 90;
            double wTran = 120;
            double wDb = 130;
            double wKr = 130;
            double wT = 40;

            // padding cell kanan-kiri total kira-kira 12 px per kolom
            double paddingPerCol = 12;
            int colCount = 7;
            double paddingTotal = paddingPerCol * colCount;

            // Hitung lebar kolom Keterangan = sisa
            double fixedSum = wNo + wTgl + wTran + wDb + wKr + wT;
            double wKet = Math.Max(120, availableWidth - fixedSum - paddingTotal);

            // Pastikan tidak negatif (kalau portrait sempit)
            if (wKet < 120)
            {
                // fallback: kompres debet/kredit sedikit biar muat
                double need = 120 - wKet;
                double takeEach = Math.Min(need / 2, 30); // minimal kurangi 30px masing2
                wDb = Math.Max(100, wDb - takeEach);
                wKr = Math.Max(100, wKr - takeEach);
                wKet = Math.Max(120, availableWidth - (wNo + wTgl + wTran + wDb + wKr + wT) - paddingTotal);
            }

            // Tambah kolom dengan lebar final
            table.Columns.Add(new TableColumn { Width = new GridLength(wNo) });
            table.Columns.Add(new TableColumn { Width = new GridLength(wTgl) });
            table.Columns.Add(new TableColumn { Width = new GridLength(wTran) });
            table.Columns.Add(new TableColumn { Width = new GridLength(wKet) });
            table.Columns.Add(new TableColumn { Width = new GridLength(wDb) });
            table.Columns.Add(new TableColumn { Width = new GridLength(wKr) });
            table.Columns.Add(new TableColumn { Width = new GridLength(wT) });

            var rg = new TableRowGroup();
            table.RowGroups.Add(rg);

            // --- BARIS HEADER KOLOM
            var head = new TableRow();
            rg.Rows.Add(head);

            void HCell(string text, bool isFirst = false)
            {
                head.Cells.Add(new TableCell(new Paragraph(new Run(text))
                {
                    Margin = new Thickness(0),
                    TextAlignment = TextAlignment.Left
                })
                {
                    Padding = new Thickness(6, 3, 6, 3),
                    BorderBrush = Brushes.Black,
                    BorderThickness = new Thickness(isFirst ? 1 : 0, 0, 1, 1.5) // kiri (untuk NO.), kanan tipis, bawah tebal
                    ,
                    FontWeight = FontWeights.Bold
                });
            }

            HCell("NO.", isFirst: true);
            HCell("TANGGAL");
            HCell("NO.TRANSAKSI");
            HCell("KETERANGAN");
            HCell("DEBET");
            HCell("KREDIT");
            HCell("T");

            // --- BODY
            // util buat bikin cell cepat
            TableCell Cell(string text, TextAlignment align = TextAlignment.Left)
            {
                var p = new Paragraph(new Run(text)) { Margin = new Thickness(0), TextAlignment = align };
                return new TableCell(p)
                {
                    Padding = new Thickness(6, 2, 6, 2),
                    BorderBrush = Brushes.Black,
                    BorderThickness = new Thickness(0, 0, 1, 1)
                };
            }


            foreach (var r in rows)
            {
                // Keterangan bisa panjang → wrap jadi beberapa paragraph dalam 1 TableCell,
                // tapi biar kolom lain tidak “ikut wrap”, kita duplikasi baris:
                var ketLines = (r.Ket ?? "").Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
                int lines = Math.Max(1, ketLines.Length);

                for (int i = 0; i < lines; i++)
                {
                    var tr = new TableRow();
                    rg.Rows.Add(tr);

                    bool first = (i == 0);
                    string no = first ? r.NoTampil : "";
                    string tgl = first ? r.TanggalTampil : "";
                    string notrn = first ? r.NoTranTampil : "";
                    string debet = first ? r.DebetStr : "";
                    string kredit = first ? r.KreditStr : "";
                    string tipe = first ? r.Type : "";
                    string ket = ketLines.ElementAtOrDefault(i) ?? "";

                    // No. | Tanggal | No.Transaksi | Keterangan | Debet | Kredit | T
                    tr.Cells.Add(Cell(no, TextAlignment.Left));
                    tr.Cells.Add(Cell(tgl, TextAlignment.Left));
                    tr.Cells.Add(Cell(notrn, TextAlignment.Left));

                    // Keterangan: wrap kiri, tidak ada vert-sep kanan biar terlihat lega
                    tr.Cells.Add(Cell(ket, TextAlignment.Left));

                    // Angka kanan
                    tr.Cells.Add(Cell(debet, TextAlignment.Right));
                    tr.Cells.Add(Cell(kredit, TextAlignment.Right));

                    // Tipe tengah
                    tr.Cells.Add(Cell(tipe, TextAlignment.Center));
                }
            }

            // --- FOOTER
            doc.Blocks.Add(new Paragraph(new Run($"Dicetak: {DateTime.Now:dd-MM-yyyy HH:mm}"))
            {
                FontSize = 10,
                Foreground = Brushes.Gray,
                Margin = new Thickness(0, 12, 0, 0)
            });

            return doc;
        }


    }


}
