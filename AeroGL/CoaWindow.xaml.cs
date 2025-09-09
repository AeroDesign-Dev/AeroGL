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

        public CoaWindow()
        {
            InitializeComponent();
            Loaded += async (_, __) => await RefreshData();
            KeyDown += Grid_KeyDown; // hotkeys global
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
            var q = TxtSearch.Text?.Trim();
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
