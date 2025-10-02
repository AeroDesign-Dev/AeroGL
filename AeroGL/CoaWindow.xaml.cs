using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using AeroGL.Core;
using AeroGL.Data;

namespace AeroGL
{
    public partial class CoaWindow : Window
    {
        private readonly ICoaRepository _coaRepo = new CoaRepository();
        private readonly ICoaBalanceRepository _balRepo = new CoaBalanceRepository();

        private ObservableCollection<Coa> _items = new ObservableCollection<Coa>();
        private ICollectionView _view;

        private ObservableCollection<Coa> _suggest = new ObservableCollection<Coa>();
        private System.Windows.Threading.DispatcherTimer _suggestTimer;
        private string _pendingQuery = "";


        public CoaWindow()
        {
            InitializeComponent();
            _suggestTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(200) // debounce 200ms
            };
            _suggestTimer.Tick += async (_, __) =>
            {
                _suggestTimer.Stop();
                await LoadSuggestions(_pendingQuery);
            };
            Loaded += async (_, __) => await RefreshData();
            KeyDown += Grid_KeyDown; // hotkeys global
        }
        private async Task LoadSuggestions(string q)
        {
            _suggest.Clear();

            if (string.IsNullOrWhiteSpace(q))
            {
                CmbSearch.IsDropDownOpen = false;
                return;
            }

            // Heuristik: jika user ketik pola kode (misal "100." atau "100.001")
            // prioritaskan prefix code3; kalau tidak, pakai Search general
            // NOTE: pakai repo.Search(q) yang kamu punya — biasanya sudah
            // match code3 & name. Kalau mau strict prefix, tambahkan SearchPrefix di repo nanti.

            var rows = await _coaRepo.Search(q);

            // Optional: sort yang code-prefix dulu biar relevan (simple scoring)
            var list = rows
                .OrderByDescending(x => (x.Code3?.StartsWith(q, StringComparison.OrdinalIgnoreCase) ?? false))
                .ThenBy(x => x.Code3)
                .Take(30) // batasi 30 hasil
                .ToList();

            foreach (var it in list) _suggest.Add(it);

            CmbSearch.IsDropDownOpen = _suggest.Count > 0;
        }

        // dapatkan TextBox internal dari ComboBox supaya bisa listen teks
        private void CmbSearch_Loaded(object sender, RoutedEventArgs e)
        {
            var tb = (CmbSearch.Template.FindName("PART_EditableTextBox", CmbSearch) as System.Windows.Controls.TextBox);
            if (tb != null)
                tb.TextChanged += CmbSearch_TextChanged;

            CmbSearch.ItemsSource = _suggest;
        }

        // ketik = debounce → query
        private void CmbSearch_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            _pendingQuery = CmbSearch.Text?.Trim() ?? "";
            _suggestTimer.Stop();
            _suggestTimer.Start();
        }

        // klik salah satu suggestion
        private void CmbSearch_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            var chosen = CmbSearch.SelectedItem as Coa;
            if (chosen != null)
            {
                MoveTo(chosen.Code3);
                CmbSearch.IsDropDownOpen = false;
            }
        }

        // ====== Data / Binding ======
        private async Task RefreshData(string moveToCode3 = null)
        {
            var rows = await _coaRepo.All();                         // Dapper → enum mapping OK
            _items = new ObservableCollection<Coa>(rows);
            _view = CollectionViewSource.GetDefaultView(_items);
            _view.CurrentChanged += async (_, __) => await LoadYearAndMonths(); // reload saat ganti akun
            DataContext = _view;

            if (_items.Count > 0) _view.MoveCurrentToFirst();
            UpdatePos();

            if (!string.IsNullOrEmpty(moveToCode3))
                MoveTo(moveToCode3);
            else
                await LoadYearAndMonths();                           // initial
        }

        private Coa Current() => _view?.CurrentItem as Coa;

        private void UpdatePos()
        {
            if (_view == null) return;
            TxtPos.Text = $"{_view.CurrentPosition + 1} / {_items.Count}";
        }

        private void MoveTo(string code3)
        {
            if (_items == null || _items.Count == 0) return;
            var target = _items.FirstOrDefault(x => x.Code3 == code3);
            if (target != null) _view.MoveCurrentTo(target);
            UpdatePos();
        }

        // ====== Tahun + 12 Bulan ======
        private async Task LoadYearAndMonths()
        {
            var cur = Current();
            if (cur == null)
            {
                CmbYear.ItemsSource = null;
                GridMonths.ItemsSource = MakeEmptyMonths();
                return;
            }

            var years = await _balRepo.YearsAvailable(cur.Code3);
            if (years.Count == 0)
            {
                CmbYear.ItemsSource = new[] { 0 };
                CmbYear.SelectedItem = 0;
                GridMonths.ItemsSource = MakeEmptyMonths();
                return;
            }

            CmbYear.ItemsSource = years;
            CmbYear.SelectedItem = years.Max();
            await LoadMonths();
        }

        private async Task LoadMonths()
        {
            var cur = Current();
            if (cur == null) return;
            if (!(CmbYear.SelectedItem is int year)) return;

            // ambil ember tahun tsb; mungkin ada Month=0..12
            var rows = await _balRepo.ListByYear(cur.Code3, year);
            var byMonth = rows.ToDictionary(r => r.Month, r => r);

            var list = new List<MonthRow>(12);
            for (int m = 1; m <= 12; m++)
            {
                byMonth.TryGetValue(m, out var b);
                decimal saldo  = b?.Saldo  ?? 0m;
                decimal debet  = b?.Debet  ?? 0m;
                decimal kredit = b?.Kredit ?? 0m;
                decimal balance = ComputeBalance(cur.Type, saldo, debet, kredit);
                list.Add(new MonthRow { Month = m, Saldo = saldo, Debet = debet, Kredit = kredit, Balance = balance });
            }

            GridMonths.ItemsSource = list;
        }

        // enum-friendly
        private static decimal ComputeBalance(AccountType type, decimal saldo, decimal debet, decimal kredit)
            => (type == AccountType.Debit)
                ? saldo + debet - kredit
                : saldo - debet + kredit;

        private static List<MonthRow> MakeEmptyMonths()
        {
            var list = new List<MonthRow>(12);
            for (int m = 1; m <= 12; m++) list.Add(new MonthRow { Month = m });
            return list;
        }

        private async void CmbYear_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
            => await LoadMonths();

        // ====== Search/Refresh ======
        private async void BtnSearch_Click(object sender, RoutedEventArgs e)
        {
            var q = CmbSearch.Text?.Trim();
            var rows = string.IsNullOrEmpty(q) ? await _coaRepo.All() : await _coaRepo.Search(q);
            _items = new ObservableCollection<Coa>(rows);
            _view = CollectionViewSource.GetDefaultView(_items);
            _view.CurrentChanged += async (_, __) => await LoadYearAndMonths();
            DataContext = _view;
            if (_items.Count > 0) _view.MoveCurrentToFirst();
            UpdatePos();
            await LoadYearAndMonths();
        }

        private async void BtnRefresh_Click(object sender, RoutedEventArgs e)
            => await RefreshData();

        // ====== Navigasi ======
        private void BtnPrev_Click(object sender, RoutedEventArgs e)
        {
            if (_view == null) return;
            if (!_view.MoveCurrentToPrevious()) _view.MoveCurrentToFirst();
            UpdatePos();
        }

        private void BtnNext_Click(object sender, RoutedEventArgs e)
        {
            if (_view == null) return;
            if (!_view.MoveCurrentToNext()) _view.MoveCurrentToLast();
            UpdatePos();
        }

        // ====== CRUD ======
        private async void BtnAdd_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new CoaEditDialog(null) { Owner = this };
            if (dlg.ShowDialog() == true)
            {
                await _coaRepo.Upsert(dlg.Result);
                await RefreshData(moveToCode3: dlg.Result.Code3);
            }
        }

        private async void BtnEdit_Click(object sender, RoutedEventArgs e)
        {
            var cur = Current();
            if (cur == null) return;

            var dlg = new CoaEditDialog(cur) { Owner = this };
            if (dlg.ShowDialog() == true)
            {
                await _coaRepo.Upsert(dlg.Result);
                await RefreshData(moveToCode3: dlg.Result.Code3);
            }
        }

        private async void BtnDelete_Click(object sender, RoutedEventArgs e)
        {
            var cur = Current();
            if (cur == null) return;

            if (MessageBox.Show($"Hapus akun {cur.Code3}?", "Konfirmasi",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

            try
            {
                await _coaRepo.Delete(cur.Code3);
                await RefreshData();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Gagal hapus", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ====== Hotkeys ala DOS ======
        private void Grid_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.F2)       { BtnAdd_Click(sender, e); e.Handled = true; }
            else if (e.Key == Key.Enter) { BtnEdit_Click(sender, e); e.Handled = true; }
            else if (e.Key == Key.Delete){ BtnDelete_Click(sender, e); e.Handled = true; }
            else if (e.Key == Key.F5)    { BtnRefresh_Click(sender, e); e.Handled = true; }
            else if (e.Key == Key.Left)  { BtnPrev_Click(sender, e); e.Handled = true; }
            else if (e.Key == Key.Right) { BtnNext_Click(sender, e); e.Handled = true; }
        }
    }

    // model untuk grid 12 bulan
    public sealed class MonthRow
    {
        public int Month { get; set; }
        public decimal Saldo { get; set; }
        public decimal Debet { get; set; }
        public decimal Kredit { get; set; }
        public decimal Balance { get; set; }
    }
}
