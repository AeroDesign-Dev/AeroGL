using AeroGL.Core;
using AeroGL.Data;
using Dapper; // hanya untuk contoh transaksi; hapus kalau tidak perlu
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Shapes;

namespace AeroGL
{
    public partial class JournalWindow : Window
    {
        private readonly IJournalHeaderRepository _hdrRepo = new JournalHeaderRepository();
        private readonly IJournalLineRepository _lineRepo = new JournalLineRepository();
        private readonly ICoaRepository _coaRepo = new CoaRepository();
        private readonly Dictionary<string, string> _nameCache = new Dictionary<string, string>();

        private bool _headerConfirmed = false;
        private string _currentNoTran = null; // hasil nomor transaksi sesudah confirm
        private JournalHeader _tempHeader = null; // draft header yang “dipegang”
        private bool _isSaving = false;

        private async Task<string> ResolveAccountNameByCode2(string code2)
        {
            if (string.IsNullOrWhiteSpace(code2)) return "";
            string name;
            if (_nameCache.TryGetValue(code2, out name)) return name;

            var coa = await _coaRepo.Get(code2 + ".001");
            name = (coa != null) ? (coa.Name ?? "") : "";
            _nameCache[code2] = name;
            return name;
        }


        private ObservableCollection<JournalHeader> _headers = new ObservableCollection<JournalHeader>();
        private ICollectionView _view; // navigator header
        private Mode _mode = Mode.View;

        // keranjang draft saat NEW
        private ObservableCollection<LineDraft> _draft = new ObservableCollection<LineDraft>();

        public JournalWindow()
        {
            InitializeComponent();
            Loaded += async (s, e) => await LoadHeaders(null);
            KeyDown += Window_KeyDown;

            SetHeaderEditable(false);
            SetGridReadonly(true);
        }

        // ===================== MODE =====================
        private enum Mode { View, New }

        private void EnterViewMode()
        {
            _mode = Mode.View;
            SetHeaderEditable(false);
            SetGridReadonly(true);
            BtnConfirmHeader.IsEnabled = false; // <—
            BtnSave.IsEnabled = false;          // <—
            UpdateStatus("VIEW");
        }

        private void EnterNewMode()
        {
            _mode = Mode.New;
            TxtNo1.Text = TxtNo2.Text = TxtNo3.Text = "";
            CmbType.SelectedIndex = 0;
            DpTanggal.SelectedDate = DateTime.Today;
            TxtTotD.Text = TxtTotK.Text = "0.00";

            SetHeaderEditable(true);
            BtnConfirmHeader.IsEnabled = true;  // <—
            BtnSave.IsEnabled = false;          // <—

            _draft = new ObservableCollection<LineDraft>();
            _draft.CollectionChanged += (s, e) =>
            {
                if (e.NewItems != null)
                    foreach (var it in e.NewItems) HookDraft(it as LineDraft);
            };

            GridLines.ItemsSource = _draft;
            SetGridReadonly(true); // grid tetap readonly; ADD lewat dialog

            UpdateStatus("NEW — Isi header lalu Confirm. Setelah itu tambah baris sampai BALANCED.");
            TxtNo1.Focus();
        }

        private void SetHeaderEditable(bool editable)
        {
            TxtNo1.IsReadOnly = !editable;
            TxtNo2.IsReadOnly = !editable;
            TxtNo3.IsReadOnly = !editable;
            CmbType.IsEnabled = editable;
            DpTanggal.IsEnabled = editable;
        }

        private void SetGridReadonly(bool ro)
        {
            GridLines.IsReadOnly = ro;
        }

        // ===================== Helpers UI/DB =====================

        private string BuildNoTran()
        {
            var a = (TxtNo1.Text ?? "").Trim();
            var b = (TxtNo2.Text ?? "").Trim();
            var c = (TxtNo3.Text ?? "").Trim();
            if (string.IsNullOrEmpty(a) && string.IsNullOrEmpty(b) && string.IsNullOrEmpty(c)) return "";
            return string.Format("{0}/{1}/{2}", a, b, c);
        }

        private JournalHeader Cur()
        {
            return _view == null ? null : _view.CurrentItem as JournalHeader;
        }

        private void UpdateStatus(string extra)
        {
            string pos = (_view == null) ? "" : string.Format("{0}/{1}", _view.CurrentPosition + 1, _headers.Count);
            TxtStatus.Text = string.IsNullOrEmpty(extra) ? pos : (string.IsNullOrEmpty(pos) ? extra : pos + "   " + extra);
        }

        private void RenderCurrentHeader()
        {
            if (_mode == Mode.New) return; // saat NEW, jangan render dari DB

            var h = Cur();
            if (h == null)
            {
                TxtNo1.Text = TxtNo2.Text = TxtNo3.Text = "";
                CmbType.SelectedIndex = 0;
                DpTanggal.SelectedDate = DateTime.Today;
                TxtTotD.Text = TxtTotK.Text = "0.00";
                GridLines.ItemsSource = new List<JournalLineRecord>();
                UpdateStatus("Tidak ada data");
                return;
            }

            var parts = (h.NoTran ?? "").Split('/');
            TxtNo1.Text = parts.Length > 0 ? parts[0] : "";
            TxtNo2.Text = parts.Length > 1 ? parts[1] : "";
            TxtNo3.Text = parts.Length > 2 ? parts[2] : "";

            CmbType.SelectedIndex = (h.Type == "M") ? 1 : 0;

            DateTime dt;
            if (!DateTime.TryParse(h.Tanggal, out dt)) dt = DateTime.Today;
            DpTanggal.SelectedDate = dt;

            TxtTotD.Text = h.TotalDebet.ToString("N2");
            TxtTotK.Text = h.TotalKredit.ToString("N2");

            LoadLines(h.NoTran).Wait();
            UpdateStatus("VIEW");
        }

        private async Task LoadLines(string noTran)
        {
            var rows = await _lineRepo.ListByNoTran(noTran);

            // paralel ringan resolve nama dari COA (code2 + ".001")
            var tasks = rows.Select(async r => new LineVM
            {
                Id = r.Id,
                Code2 = r.Code2,
                AccountName = await ResolveAccountNameByCode2(r.Code2),
                Side = r.Side,
                Amount = r.Amount,
                Narration = r.Narration
            }).ToArray();

            var vms = await Task.WhenAll(tasks);
            GridLines.ItemsSource = vms;
        }

        private async Task LoadHeaders(string q)
        {
            List<JournalHeader> rows = string.IsNullOrWhiteSpace(q)
                ? await _hdrRepo.All()
                : await _hdrRepo.Search(q);

            _headers = new ObservableCollection<JournalHeader>(rows);
            _view = CollectionViewSource.GetDefaultView(_headers);
            if (_view != null)
            {
                _view.CurrentChanged += (s, e) => RenderCurrentHeader();
                if (_headers.Count > 0) _view.MoveCurrentToFirst();
            }
            EnterViewMode();
            RenderCurrentHeader();
        }

        private void BtnPrev_Click(object sender, RoutedEventArgs e)
        {
            if (_mode == Mode.New) return;
            if (_view == null) return;
            if (!_view.MoveCurrentToPrevious()) _view.MoveCurrentToFirst();
            RenderCurrentHeader();
        }

        private void BtnNext_Click(object sender, RoutedEventArgs e)
        {
            if (_mode == Mode.New) return;
            if (_view == null) return;
            if (!_view.MoveCurrentToNext()) _view.MoveCurrentToLast();
            RenderCurrentHeader();
        }

        private void BtnQuit_Click(object sender, RoutedEventArgs e) => Close();

        // ===================== Bottom Buttons =====================

        // ADD: di VIEW → masuk NEW; di NEW → tambah baris draft
        private void BtnAdd_Click(object sender, RoutedEventArgs e)
        {
            // Kalau masih VIEW (preview data), klik Add = mulai transaksi baru (mode NEW)
            if (_mode == Mode.View && !_headerConfirmed)
            {
                EnterNewMode();
                return;
            }

            // Sudah NEW tapi header belum confirm → ingatkan user
            if (_mode == Mode.New && !_headerConfirmed)
            {
                MessageBox.Show("Confirm Header dulu sebelum menambah baris.", "Info",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // NEW + header sudah confirm → buka dialog tambah baris
            var dlg = new JournalLineDialog { Owner = this };
            if (dlg.ShowDialog() == true)
            {
                var line = dlg.Result;
                if (line == null) return;

                var draft = new LineDraft
                {
                    Code2 = line.Code2,
                    Side = line.Side,
                    Amount = line.Amount,
                    Narration = line.Narration
                };

                _ = Task.Run(async () =>
                {
                    var name = await ResolveAccountNameByCode2(draft.Code2);
                    Dispatcher.Invoke(() => draft.AccountName = name);
                });

                _draft.Add(draft);
                GridLines.SelectedIndex = _draft.Count - 1;
                GridLines.ScrollIntoView(GridLines.SelectedItem);

                UpdateTotalsFromDraft();
                UpdateSaveButtonState();
            }
        }

        private bool IsBalancedDraft(out decimal d, out decimal k)
        {
            var t = CalcTotalsFromDraft();
            d = t.D; k = t.K;
            return d == k && d > 0m;
        }

        private void UpdateSaveButtonState()
        {
            if (!_headerConfirmed)
            {
                BtnSave.IsEnabled = false;
                return;
            }

            Dispatcher.Invoke(() =>
            {
                BtnSave.IsEnabled = IsBalancedDraft(out _, out _);
            });
        }

        // ===================== Draft totals (live) =====================
        private void UpdateTotalsFromDraft()
        {
            if (_mode != Mode.New && !_headerConfirmed) return;

            var (d, k) = CalcTotalsFromDraft();

            TxtTotD.Text = d.ToString("N2");
            TxtTotK.Text = k.ToString("N2");

            var diff = d - k;
            var note = diff == 0m ? "BALANCED" : ("UNBALANCED: " + diff.ToString("N2"));
            UpdateStatus((_headerConfirmed ? "NEW(Header OK)" : "NEW") + " — " + note);

            UpdateSaveButtonState();
        }

        // EDIT: di VIEW → toggle header edit? (sesuai flow baru kita pakai utk SAVE saat NEW)
        private async void BtnEdit_Click(object sender, RoutedEventArgs e)
        {
            if (_mode == Mode.New)
            {
                // Dulu: await SaveNewJournal();
                // Ganti: arahkan user pakai tombol Save, atau panggil BtnSave_Click jika mau
                MessageBox.Show("Gunakan tombol Save untuk menyimpan transaksi baru.", "Info",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            MessageBox.Show("Mode EDIT header existing belum diaktifkan. Gunakan NEW untuk menambah transaksi.",
                "Info", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private async void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            if (!_headerConfirmed || _tempHeader == null)
            {
                MessageBox.Show("Header belum di-confirm.", "Info",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var (d, k) = CalcTotalsFromDraft();
            if (d <= 0m || d != k)
            {
                MessageBox.Show("Total Debet dan Kredit HARUS balance.", "Validasi",
                    MessageBoxButton.OK, MessageBoxImage.Exclamation);
                return;
            }

            if (_isSaving) return;
            _isSaving = true;

            try
            {
                using (var cn = Db.Open())
                using (var tx = cn.BeginTransaction())
                {
                    // 1) INSERT header TANPA TOTAL (biarkan default 0; trigger yang isi)
                    await cn.ExecuteAsync(
                        "INSERT INTO JournalHeader(NoTran,Tanggal,Type,Memo) VALUES(@NoTran,@Tanggal,@Type,@Memo)",
                        new
                        {
                            _tempHeader.NoTran,
                            _tempHeader.Tanggal,
                            _tempHeader.Type,
                            _tempHeader.Memo
                        }, tx);

                    // 2) INSERT lines → trigger akan update TotalDebet/TotalKredit otomatis
                    foreach (var r in _draft)
                    {
                        await cn.ExecuteAsync(
                            "INSERT INTO JournalLine(NoTran,Code2,Side,Amount,Narration) VALUES(@NoTran,@Code2,@Side,@Amount,@Narration)",
                            new
                            {
                                NoTran = _tempHeader.NoTran,
                                Code2 = r.Code2,
                                Side = r.Side,
                                Amount = r.Amount,
                                Narration = r.Narration ?? ""
                            }, tx);
                    }

                    tx.Commit();
                }

                // 3) Refresh ke VIEW & posisikan ke transaksi yang barusan disimpan
                var justSavedNo = _tempHeader.NoTran;

                _headerConfirmed = false;
                _tempHeader = null;
                _currentNoTran = null;
                _draft.Clear();

                await LoadHeaders(null);
                var target = _headers.FirstOrDefault(x => x.NoTran == justSavedNo);
                if (target != null) _view.MoveCurrentTo(target);

                EnterViewMode();
                RenderCurrentHeader();
                MessageBox.Show("Journal tersimpan.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Gagal menyimpan", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                _isSaving = false;
            }
        }
        // DEL: di VIEW → hapus line terpilih (konfirmasi); di NEW → remove draft row
        private async void BtnDel_Click(object sender, RoutedEventArgs e)
        {
            // NEW mode: tidak boleh delete (belum ada yang disimpan ke DB)
            if (_mode == Mode.New)
            {
                MessageBox.Show("Transaksi belum disimpan. Batalkan NEW (Esc) kalau mau keluar.",
                                "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var h = Cur();
            if (h == null) return;

            // Konfirmasi jelas: hapus seluruh jurnal
            if (MessageBox.Show($"Hapus SELURUH jurnal '{h.NoTran}' (header + semua baris)?",
                                "Konfirmasi",
                                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;

            try
            {
                using (var cn = Db.Open())
                using (var tx = cn.BeginTransaction())
                {
                    // 1) Hapus semua lines untuk NoTran ini
                    await cn.ExecuteAsync("DELETE FROM JournalLine WHERE NoTran=@no", new { no = h.NoTran }, tx);

                    // 2) Hapus header-nya
                    await cn.ExecuteAsync("DELETE FROM JournalHeader WHERE NoTran=@no", new { no = h.NoTran }, tx);

                    tx.Commit();
                }

                // Refresh view setelah delete
                var deletedNo = h.NoTran;
                await LoadHeaders(null);
                if (_headers.Count > 0) _view.MoveCurrentToFirst();
                EnterViewMode();
                RenderCurrentHeader();

                MessageBox.Show($"Jurnal '{deletedNo}' dihapus.", "Info",
                                MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Gagal menghapus", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void BtnFind_Click(object sender, RoutedEventArgs e)
        {
            if (_mode == Mode.New)
            {
                MessageBox.Show("Sedang membuat transaksi baru. Simpan atau batalkan dulu.", "Info",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dlg = new FindJournalDialogPlaceholder(_hdrRepo) { Owner = this };
            if (dlg.ShowDialog() == true)
            {
                var chosen = dlg.SelectedNoTran;
                await LoadHeaders(null);
                var target = _headers.FirstOrDefault(x => x.NoTran == chosen);
                if (target != null) { _view.MoveCurrentTo(target); RenderCurrentHeader(); }
            }
        }

        // ===================== SAVE NEW JOURNAL =====================

        

        private void HookDraft(LineDraft d)
        {
            if (d == null) return;

            d.PropertyChanged += async (ss, ee) =>
            {
                // update nama saat code2 berubah
                if (ee.PropertyName == "Code2")
                {
                    var name = await ResolveAccountNameByCode2(d.Code2);
                    d.AccountName = name;
                }

                // hitung ulang total saat side/amount berubah
                if (ee.PropertyName == "Side" || ee.PropertyName == "Amount")
                {
                    UpdateTotalsFromDraft();
                }
            };
        }

        private async Task RefreshTotals(string noTran)
        {
            var h = await _hdrRepo.Get(noTran);
            if (h != null)
            {
                TxtTotD.Text = h.TotalDebet.ToString("N2");
                TxtTotK.Text = h.TotalKredit.ToString("N2");
            }
        }

        // ===================== Hotkeys =====================

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (_mode == Mode.New && e.Key == Key.Escape) { EnterViewMode(); RenderCurrentHeader(); e.Handled = true; return; }  
            if (e.Key == Key.F2) { BtnAdd_Click(sender, e); e.Handled = true; return; } // Add / Add row
            if (e.Key == Key.Enter && _mode == Mode.View) { BtnEdit_Click(sender, e); e.Handled = true; return; } // (optional)
            if (e.Key == Key.Delete) { BtnDel_Click(sender, e); e.Handled = true; return; }
            if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.F) { BtnFind_Click(sender, e); e.Handled = true; return; }
            if (_mode == Mode.View && e.Key == Key.Left) { BtnPrev_Click(sender, e); e.Handled = true; return; }
            if (_mode == Mode.View && e.Key == Key.Right) { BtnNext_Click(sender, e); e.Handled = true; return; }
        }

        private void GridLines_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {

        }

        private (decimal D, decimal K) CalcTotalsFromDraft()
        {
            var d = _draft.Where(x => string.Equals(x.Side, "D", StringComparison.OrdinalIgnoreCase))
                          .Sum(x => x.Amount);
            var k = _draft.Where(x => string.Equals(x.Side, "K", StringComparison.OrdinalIgnoreCase))
                          .Sum(x => x.Amount);
            return (d, k);
        }

        private (bool ok, string msg, string no, string jenis, string tglIso) ValidateHeaderInputs()
        {
            var no = BuildNoTran();
            if (string.IsNullOrWhiteSpace(no)) return (false, "Isi No. Transaksi (3 kotak).", null, null, null);

            var cmb = CmbType.SelectedItem as ComboBoxItem;
            var jenis = (cmb?.Content ?? "J").ToString();

            var tgl = DpTanggal.SelectedDate ?? DateTime.Today;
            var tglIso = tgl.ToString("yyyy-MM-dd");

            return (true, null, no, jenis, tglIso);
        }
        private async void BtnConfirmHeader_Click(object sender, RoutedEventArgs e)
        {
            // 1) validasi input header
            var v = ValidateHeaderInputs();
            if (!v.ok) { MessageBox.Show(v.msg, "Validasi", MessageBoxButton.OK, MessageBoxImage.Exclamation); return; }

            // 2) cek duplikat di DB (header final)
            var exists = await _hdrRepo.Get(v.no);
            if (exists != null)
            {
                MessageBox.Show("No. Transaksi sudah ada.", "Validasi", MessageBoxButton.OK, MessageBoxImage.Exclamation);
                return;
            }

            // 3) simpan header sementara (in-memory dulu; nanti bisa dipindah ke tabel TempHeader)
            _tempHeader = new JournalHeader
            {
                NoTran = v.no,
                Tanggal = v.tglIso,
                Type = v.jenis,
                TotalDebet = 0m,
                TotalKredit = 0m,
                Memo = "" // nanti kalau perlu
            };
            _currentNoTran = v.no;
            _headerConfirmed = true;

            // 4) kunci header dan siapkan grid untuk ADD line
            SetHeaderEditable(false);
            SetGridReadonly(true); // grid tetap readonly; ADD lewat dialog
            if (_draft == null) _draft = new ObservableCollection<LineDraft>();
            _draft.Clear();
            _draft.CollectionChanged += (s2, e2) =>
            {
                if (e2.NewItems != null)
                    foreach (var it in e2.NewItems) HookDraft(it as LineDraft);
            };
            GridLines.ItemsSource = _draft;

            // 5) update UI status
            TxtTotD.Text = "0.00";
            TxtTotK.Text = "0.00";
            UpdateStatus($"NEW — Header confirmed: {_currentNoTran}. Tekan Add untuk menambah baris.");
            BtnSave.IsEnabled = false;     // belum balanced
            BtnConfirmHeader.IsEnabled = false;
        }

    }

    // =============== DRAFT MODEL UNTUK MODE NEW ===============
    public sealed class LineDraft : INotifyPropertyChanged
    {
        private string _code2;
        public string Code2 { get { return _code2; } set { _code2 = value; OnChanged("Code2"); } }

        private string _side = "D"; // "D" / "K"
        public string Side { get { return _side; } set { _side = (value ?? "").ToUpperInvariant(); OnChanged("Side"); } }

        private decimal _amount;
        public decimal Amount { get { return _amount; } set { _amount = value; OnChanged("Amount"); } }

        private string _narr;
        public string Narration { get { return _narr; } set { _narr = value; OnChanged("Narration"); } }

        // ===== nama akun untuk grid draft =====
        private string _accName;
        public string AccountName { get { return _accName; } set { _accName = value; OnChanged("AccountName"); } }

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnChanged(string n)
        {
            var h = PropertyChanged; if (h != null) h(this, new PropertyChangedEventArgs(n));
        }
    }

    public sealed class LineVM
    {
        public long Id { get; set; }
        public string Code2 { get; set; }
        public string AccountName { get; set; }
        public string Side { get; set; }
        public decimal Amount { get; set; }
        public string Narration { get; set; }
    }

    // =============== PLACEHOLDER untuk Find (opsional) ===============
    public class FindJournalDialogPlaceholder : Window
    {
        private readonly IJournalHeaderRepository _repo;
        public string SelectedNoTran { get; private set; }

        public FindJournalDialogPlaceholder(IJournalHeaderRepository repo)
        {
            _repo = repo;
            Title = "Find Journal";
            Width = 640; Height = 420;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = System.Windows.Media.Brushes.Black;

            var root = new System.Windows.Controls.Grid();
            root.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = GridLength.Auto });

            var dg = new System.Windows.Controls.DataGrid
            {
                AutoGenerateColumns = false,
                IsReadOnly = true,
                Background = System.Windows.Media.Brushes.Transparent,
                Foreground = System.Windows.Media.Brushes.White,
                BorderThickness = new Thickness(0)
            };
            dg.Columns.Add(new System.Windows.Controls.DataGridTextColumn { Header = "No. Transaksi", Binding = new System.Windows.Data.Binding("NoTran"), Width = 180 });
            dg.Columns.Add(new System.Windows.Controls.DataGridTextColumn { Header = "Tanggal", Binding = new System.Windows.Data.Binding("Tanggal"), Width = 120 });
            dg.Columns.Add(new System.Windows.Controls.DataGridTextColumn { Header = "Tipe", Binding = new System.Windows.Data.Binding("Type"), Width = 60 });
            dg.Columns.Add(new System.Windows.Controls.DataGridTextColumn { Header = "Memo", Binding = new System.Windows.Data.Binding("Memo"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
            dg.MouseDoubleClick += (s, e) =>
            {
                var it = dg.SelectedItem as JournalHeader;
                if (it != null) { SelectedNoTran = it.NoTran; DialogResult = true; }
            };

            var close = new System.Windows.Controls.Button { Content = "Close", Margin = new Thickness(8) };
            close.Click += (s, e) => { DialogResult = false; };

            System.Windows.Controls.Grid.SetRow(dg, 0);
            System.Windows.Controls.Grid.SetRow(close, 1);
            root.Children.Add(dg);
            root.Children.Add(close);
            Content = root;

            Loaded += async (s, e) => { var list = await _repo.All(); dg.ItemsSource = list; };
        }
    }
}
