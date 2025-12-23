using AeroGL.Core;
using AeroGL.Data;
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;

namespace AeroGL
{
    public partial class EntryTabelWindow : Window
    {
        private readonly ICoaRepository _repo = new CoaRepository();
        private readonly DispatcherTimer _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };

        // Tracking element aktif
        private TextBox _activeTxt;
        private Popup _activePop;
        private ListBox _activeList;
        private TextBlock _activeLbl; // Buat update nama

        public EntryTabelWindow()
        {
            InitializeComponent();

            Loaded += async (s, e) =>
            {
                // Load data existing
                TxtLabaDitahan.Text = AccountConfig.PrefixLabaDitahan;
                TxtLabaBerjalan.Text = AccountConfig.PrefixLabaBerjalan;

                // Coba resolve nama akun kalau datanya ada
                await ResolveName(TxtLabaDitahan.Text, LblNamaDitahan);
                await ResolveName(TxtLabaBerjalan.Text, LblNamaBerjalan);

                TxtLabaDitahan.Focus();
                TxtLabaDitahan.SelectAll();
            };

            // Timer debounce search
            _debounce.Tick += async (s, e) =>
            {
                _debounce.Stop();
                if (_activeTxt != null)
                    await PerformSearch(_activeTxt.Text, _activePop, _activeList);
            };

            PreviewKeyDown += (s, e) =>
            {
                if ((PopDitahan.IsOpen || PopBerjalan.IsOpen) && (e.Key == Key.Enter || e.Key == Key.Down)) return;
                if (e.Key == Key.Escape) { Close(); e.Handled = true; }
                else if (e.Key == Key.Enter) { BtnSimpan_Click(s, e); e.Handled = true; }
            };
        }

        // === LOGIC TEXT CHANGED & SEARCH ===

        private void Txt_TextChanged(object sender, TextChangedEventArgs e)
        {
            var txt = sender as TextBox;
            if (txt == null) return;

            if (txt == TxtLabaDitahan) SetActive(TxtLabaDitahan, PopDitahan, LstDitahan, LblNamaDitahan);
            else if (txt == TxtLabaBerjalan) SetActive(TxtLabaBerjalan, PopBerjalan, LstBerjalan, LblNamaBerjalan);

            _debounce.Stop();
            _debounce.Start();

            // Kosongkan nama kalau text dihapus user, biar gak bingung
            if (_activeLbl != null) _activeLbl.Text = "...";
        }

        private void SetActive(TextBox t, Popup p, ListBox l, TextBlock lbl)
        {
            _activeTxt = t; _activePop = p; _activeList = l; _activeLbl = lbl;
        }

        private async Task PerformSearch(string query, Popup pop, ListBox list)
        {
            if (string.IsNullOrWhiteSpace(query)) { pop.IsOpen = false; return; }
            try
            {
                var results = await _repo.Search(query);
                var sorted = results.OrderBy(x => x.Code3).Take(20).ToList();

                if (sorted.Count > 0) { list.ItemsSource = sorted; pop.IsOpen = true; }
                else { pop.IsOpen = false; }
            }
            catch { pop.IsOpen = false; }
        }

        // === LOGIC SELECTION (Full Code + Name) ===

        private void SelectItem(ListBox lb, TextBox targetTxt, Popup targetPop, TextBlock targetLbl)
        {
            var coa = lb.SelectedItem as Coa;
            if (coa != null)
            {
                // 1. Masukin FULL CODE ke TextBox (Sesuai request lo)
                targetTxt.Text = coa.Code3;
                targetTxt.CaretIndex = targetTxt.Text.Length;

                // 2. Tampilin NAMA AKUN di sebelahnya
                targetLbl.Text = coa.Name;

                targetPop.IsOpen = false;
            }
        }

        private void Lst_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (sender == LstDitahan) SelectItem(LstDitahan, TxtLabaDitahan, PopDitahan, LblNamaDitahan);
            else if (sender == LstBerjalan) SelectItem(LstBerjalan, TxtLabaBerjalan, PopBerjalan, LblNamaBerjalan);
        }

        private void Lst_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            var lst = sender as ListBox;
            bool isDitahan = (lst == LstDitahan);

            if (e.Key == Key.Enter)
            {
                if (isDitahan) SelectItem(LstDitahan, TxtLabaDitahan, PopDitahan, LblNamaDitahan);
                else SelectItem(LstBerjalan, TxtLabaBerjalan, PopBerjalan, LblNamaBerjalan);

                (isDitahan ? TxtLabaDitahan : TxtLabaBerjalan).Focus();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                (isDitahan ? PopDitahan : PopBerjalan).IsOpen = false;
                (isDitahan ? TxtLabaDitahan : TxtLabaBerjalan).Focus();
                e.Handled = true;
            }
        }

        private void Txt_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            var txt = sender as TextBox;
            ListBox lst = (txt == TxtLabaDitahan) ? LstDitahan : LstBerjalan;
            Popup pop = (txt == TxtLabaDitahan) ? PopDitahan : PopBerjalan;

            if (e.Key == Key.Down && pop.IsOpen && lst.Items.Count > 0)
            {
                lst.SelectedIndex = 0;
                (lst.ItemContainerGenerator.ContainerFromIndex(0) as ListBoxItem)?.Focus();
                e.Handled = true;
            }
        }

        // === UTILS ===

        private async void Txt_LostFocus(object sender, RoutedEventArgs e)
        {
            var txt = sender as TextBox;
            TextBlock lbl = (txt == TxtLabaDitahan) ? LblNamaDitahan : LblNamaBerjalan;

            // Coba resolve nama akun kalau user ngetik manual trus pindah
            await ResolveName(txt.Text, lbl);

            // Close popup delay
            Task.Delay(200).ContinueWith(_ => Dispatcher.Invoke(() =>
            {
                if (!LstDitahan.IsKeyboardFocusWithin) PopDitahan.IsOpen = false;
                if (!LstBerjalan.IsKeyboardFocusWithin) PopBerjalan.IsOpen = false;
            }));
        }

        private async Task ResolveName(string code, TextBlock targetLbl)
        {
            if (string.IsNullOrWhiteSpace(code)) return;
            try
            {
                // Cari akun yang kodenya persis atau diawali code tsb (misal user cuma ketik 016)
                // Kita ambil yg pertama match
                var results = await _repo.Search(code);
                var match = results.FirstOrDefault(x => x.Code3.StartsWith(code));

                if (match != null) targetLbl.Text = match.Name;
                else targetLbl.Text = "(Akun tidak ditemukan)";
            }
            catch { }
        }

        // === SAVE ===

        private void BtnSimpan_Click(object sender, RoutedEventArgs e)
        {
            string d = TxtLabaDitahan.Text.Trim();
            string b = TxtLabaBerjalan.Text.Trim();

            if (string.IsNullOrEmpty(d) || string.IsNullOrEmpty(b))
            {
                MessageBox.Show("Kode akun tidak boleh kosong!", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                // REVISI: Langsung simpan FULL CODE ("016.001.001")
                // Gak perlu dipotong jadi prefix di sini.
                AccountConfig.Save(d, b);

                MessageBox.Show($"Konfigurasi tersimpan!\n\nLaba Ditahan: {d}\nLaba Berjalan: {b}",
                                "Sukses", MessageBoxButton.OK, MessageBoxImage.Information);
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error: " + ex.Message, "Gagal", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnBatal_Click(object sender, RoutedEventArgs e) => Close();
    }
}