using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AeroGL.Core;
using AeroGL.Data;

namespace AeroGL
{
    public partial class JournalLineDialog : Window
    {
        // ==== Hasil untuk caller (JournalWindow) ====
        public sealed class LineResult
        {
            public string Code2 { get; set; }     // "xxx.xxx" (2-seg)
            public string Side { get; set; }      // "D" / "K"
            public decimal Amount { get; set; }   // > 0
            public string Narration { get; set; } // multi-line bebas
        }

        public LineResult Result { get; private set; }

        // ==== (opsional) info prefill saat edit ====
        private readonly string _noTran;
        private readonly JournalLineRecord _orig;

        // ==== infra suggestion ====
        private readonly ICoaRepository _coaRepo = new CoaRepository();
        private readonly ObservableCollection<Coa> _suggest = new ObservableCollection<Coa>();
        private readonly System.Windows.Threading.DispatcherTimer _debounce =
            new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
        private string _pending = "";

        public JournalLineDialog(string noTran = null, JournalLineRecord existing = null)
        {
            InitializeComponent();

            _noTran = noTran;
            _orig = existing;

            // Prefill kalau edit
            if (_orig != null)
            {
                Title = "Ubah Baris Jurnal";
                TxtCode2.Text = _orig.Code2;
                TxtAmount.Text = _orig.Amount.ToString("N2", CultureInfo.CurrentCulture);

                var side = _orig.Side == "K" ? "K" : "D";
                var choose = CmbSide.Items.OfType<ComboBoxItem>()
                    .FirstOrDefault(x => string.Equals(x.Content as string, side, StringComparison.OrdinalIgnoreCase));
                if (choose != null) choose.IsSelected = true;

                TxtNarr.Text = (_orig.Narration ?? "");
            }
            else
            {
                Title = "Tambah Baris Jurnal";
            }

            // Wiring events setelah visual siap
            Loaded += (s, e) =>
            {
                TxtCode2.TextChanged += TxtCode2_TextChanged;
                TxtCode2.LostFocus += TxtCode2_LostFocus;
                TxtCode2.PreviewKeyDown += TxtCode2_PreviewKeyDown;

                LstSuggest.ItemsSource = _suggest;
                TxtCode2.Focus();
            };

            // Debounce suggestion
            _debounce.Tick += async (_, __) =>
            {
                _debounce.Stop();
                await LoadSuggestAsync(_pending);
            };
        }

        // ===== Suggestion =====
        private void TxtCode2_TextChanged(object sender, TextChangedEventArgs e)
        {
            _pending = (TxtCode2.Text ?? "").Trim();
            if (string.IsNullOrEmpty(_pending))
            {
                _suggest.Clear();
                PopSuggest.IsOpen = false;
                LblAccName.Text = "";
                return;
            }

            _debounce.Stop();
            _debounce.Start();
        }

        private async Task LoadSuggestAsync(string q)
        {
            try
            {
                var rows = await _coaRepo.Search(q);

                // Urutkan: prefix match Code3 → lalu Code3 ascending
                var list = rows
                    .OrderByDescending(x => (x.Code3 != null && x.Code3.StartsWith(q, StringComparison.OrdinalIgnoreCase)))
                    .ThenBy(x => x.Code3)
                    .Take(30)
                    .ToList();

                _suggest.Clear();
                foreach (var it in list) _suggest.Add(it);

                PopSuggest.IsOpen = _suggest.Count > 0;
            }
            catch
            {
                // diamkan saja di UI (hindari pesan pop-up saat ketik)
                PopSuggest.IsOpen = false;
            }
        }

        private void LstSuggest_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var chosen = LstSuggest.SelectedItem as Coa;
            if (chosen == null) return;

            // Ambil 2 segmen dari Code3 → isi ke TxtCode2
            var parts = (chosen.Code3 ?? "").Split('.');
            TxtCode2.Text = parts.Length >= 2 ? (parts[0] + "." + parts[1]) : chosen.Code3;

            PopSuggest.IsOpen = false;

            // Update nama akun pasif
            _ = ResolveAndShowAccountName();
        }

        private void LstSuggest_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            LstSuggest_SelectionChanged(sender, null);
        }

        private void TxtCode2_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // Panah bawah untuk masuk ke list saat popup terbuka
            if (e.Key == Key.Down && PopSuggest.IsOpen && LstSuggest.Items.Count > 0)
            {
                LstSuggest.Focus();
                LstSuggest.SelectedIndex = 0;
                e.Handled = true;
                return;
            }

            // Enter memilih item teratas saat popup terbuka
            if (e.Key == Key.Enter && PopSuggest.IsOpen)
            {
                var chosen = (LstSuggest.SelectedItem as Coa) ?? LstSuggest.Items.OfType<Coa>().FirstOrDefault();
                if (chosen != null)
                {
                    LstSuggest.SelectedItem = chosen; // trigger handler
                    LstSuggest_SelectionChanged(LstSuggest, null);
                    e.Handled = true;
                }
            }
        }

        private async void TxtCode2_LostFocus(object sender, RoutedEventArgs e)
        {
            await ResolveAndShowAccountName();
        }

        private async Task ResolveAndShowAccountName()
        {
            var code2 = (TxtCode2.Text ?? "").Trim();
            if (!Regex.IsMatch(code2, @"^\d{3}\.\d{3}$"))
            {
                LblAccName.Text = "";
                return;
            }

            try
            {
                // Aturan COA: hanya .001 yang valid
                var coa = await _coaRepo.Get(code2 + ".001");
                LblAccName.Text = (coa != null) ? coa.Name : "(akun tidak ditemukan)";
            }
            catch
            {
                LblAccName.Text = "";
            }
        }

        // ===== Save / Cancel =====
        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            var code2 = (TxtCode2.Text ?? "").Trim();
            var amountText = (TxtAmount.Text ?? "").Trim();

            if (!Regex.IsMatch(code2, @"^\d{3}\.\d{3}$"))
            {
                MessageBox.Show("Kode rekening (2-seg) wajib format xxx.xxx.", "Validasi",
                    MessageBoxButton.OK, MessageBoxImage.Exclamation);
                return;
            }

            decimal amount;
            if (!decimal.TryParse(amountText, NumberStyles.Number, CultureInfo.CurrentCulture, out amount) || amount <= 0m)
            {
                MessageBox.Show("Jumlah harus angka > 0.", "Validasi",
                    MessageBoxButton.OK, MessageBoxImage.Exclamation);
                return;
            }

            var sideItem = CmbSide.SelectedItem as ComboBoxItem;
            var side = (sideItem != null ? (sideItem.Content as string) : "D");
            side = string.IsNullOrEmpty(side) ? "D" : side.ToUpperInvariant();
            if (side != "D" && side != "K") side = "D";

            var narr = (TxtNarr.Text ?? "").Trim();

            Result = new LineResult
            {
                Code2 = code2,
                Side = side,
                Amount = amount,
                Narration = narr
            };

            DialogResult = true;
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
