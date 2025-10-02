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
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace AeroGL
{
    public partial class JournalWindow : Window
    {
        // ===================== MODE =====================
        private enum Mode { View, New, Edit }
        private readonly IJournalHeaderRepository _hdrRepo = new JournalHeaderRepository();
        private readonly IJournalLineRepository _lineRepo = new JournalLineRepository();
        private readonly ICoaRepository _coaRepo = new CoaRepository();
        private ICollectionView _view; // navigator header

        // keranjang draft saat NEW
        private ObservableCollection<JournalHeader> _headers = new ObservableCollection<JournalHeader>();
        private ObservableCollection<LineDraft> _draft = new ObservableCollection<LineDraft>();
        private readonly Dictionary<string, string> _nameCache = new Dictionary<string, string>();

        private bool _headerConfirmed = false;
        private string _currentNoTran = null; // hasil nomor transaksi sesudah confirm
        private JournalHeader _tempHeader = null; // draft header yang “dipegang”
        private bool _isSaving = false;

        // Tambah di atas (fields):
        private JournalHeader _oldHeaderSnapshot = null;
        private List<JournalLineRecord> _oldLinesSnapshot = null;

        // enum Mode: tambahkan Edit
        private Mode _mode = Mode.View;

        //Init
        public JournalWindow()
        {
            InitializeComponent();
            Loaded += async (s, e) => await LoadHeaders(null);
            KeyDown += Window_KeyDown;
            
            SetHeaderEditable(false);
            SetGridReadonly(true);
        }

        // ===================== Mode Transisi =====================
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
        private void EnterEditMode()
        {
            _mode = Mode.Edit;

            // Header editable (termasuk NoTran)
            SetHeaderEditable(true);
            BtnConfirmHeader.IsEnabled = false; // tidak dipakai di Edit
            BtnSave.IsEnabled = false;          // akan enable kalau balanced

            // Siapkan _draft dari data DB (snapshot lama sudah diambil sebelumnya)
            _draft = new ObservableCollection<LineDraft>();
            _draft.CollectionChanged += (s, e) =>
            {
                if (e.NewItems != null)
                    foreach (var it in e.NewItems) HookDraft(it as LineDraft);
            };

            // Prefill draft dari _oldLinesSnapshot
            if (_oldLinesSnapshot != null)
            {
                foreach (var r in _oldLinesSnapshot)
                {
                    var d = new LineDraft { Code2 = r.Code2, Side = r.Side, Amount = r.Amount, Narration = r.Narration };
                    _draft.Add(d);

                    // resolve async nama akun
                    _ = Task.Run(async () =>
                    {
                        var name = await ResolveAccountNameByCode2(d.Code2);
                        Dispatcher.Invoke(() => d.AccountName = name);
                    });
                }
            }

            GridLines.ItemsSource = _draft;
            SetGridReadonly(true); // tetap readonly; ADD/DEL via dialog & tombol

            UpdateTotalsFromDraft();
            UpdateSaveButtonState();
            UpdateStatus("EDIT — Ubah header/lines. Simpan jika BALANCED.");

            // === DEBUG ===
            DebugLog.Info("EDIT", $"Masuk Edit: oldNo={_oldHeaderSnapshot?.NoTran}, lineCount={_oldLinesSnapshot?.Count ?? 0}");
            if (_oldLinesSnapshot != null)
            {
                foreach (var x in _oldLinesSnapshot)
                    DebugLog.Info("EDIT:LINE", $"oldLine Id={x.Id}, {x.Code2} {x.Side} {x.Amount:N2}");
            }
        }

        // ===================== Resolve Account Name =====================
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

        private async Task RenderCurrentHeader()
        {
            if (_mode == Mode.New) return;

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

            DateTime dt; if (!DateTime.TryParse(h.Tanggal, out dt)) dt = DateTime.Today;
            DpTanggal.SelectedDate = dt;

            TxtTotD.Text = h.TotalDebet.ToString("N2");
            TxtTotK.Text = h.TotalKredit.ToString("N2");

            await LoadLines(h.NoTran); // ← no .Wait()
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

            // === DEBUG ===
            DebugLog.Info("LOAD", $"LoadLines noTran={noTran}, count={vms.Length}");
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

            // === DEBUG ===
            DebugLog.Info("LOAD", $"LoadHeaders count={_headers.Count}");
        }



        private async Task SaveEditedJournalAsync(
            JournalHeader oldHeader,
            List<JournalLineRecord> oldLines,
            string newNo,
            DateTime newTanggal,
            string newType,
            List<LineDraft> newLines)
        {
            if (oldHeader == null) throw new InvalidOperationException("Snapshot header lama tidak ada.");
            if (oldLines == null) throw new InvalidOperationException("Snapshot lines lama tidak ada.");

            var oldNo = oldHeader.NoTran;

            // Validasi NoTran baru (kalau ganti, tidak boleh tabrakan)
            if (!string.Equals(newNo, oldNo, StringComparison.OrdinalIgnoreCase))
            {
                var exists = await _hdrRepo.Get(newNo);
                if (exists != null) throw new InvalidOperationException("No. Transaksi baru sudah ada.");
            }

            // Validasi lines baru & balance
            if (newLines == null || newLines.Count < 2)
                throw new InvalidOperationException("Minimal 2 baris.");
            foreach (var r in newLines)
            {
                if (r.Side != "D" && r.Side != "K") throw new InvalidOperationException("Sisi harus D/K.");
                if (r.Amount <= 0m) throw new InvalidOperationException("Jumlah harus > 0.");
                if (string.IsNullOrWhiteSpace(r.Code2)) throw new InvalidOperationException("Code2 wajib.");
            }
            var totD = newLines.Where(x => x.Side == "D").Sum(x => x.Amount);
            var totK = newLines.Where(x => x.Side == "K").Sum(x => x.Amount);
            if (totD != totK) throw new InvalidOperationException("Total Debet dan Kredit harus balance.");

            DebugLog.Info("SAVE-EDIT", $"BEGIN oldNo={oldHeader.NoTran} -> newNo={newNo}, newTgl={newTanggal:yyyy-MM-dd}, newType={newType}, newLines={newLines.Count}");

            using (var cn = Db.Open())
            using (var tx = cn.BeginTransaction())
            {
                var posting = new PostingRealtimeService(cn, tx);
                var oldIso = oldHeader.Tanggal;
                foreach (var ol in oldLines)
                    await posting.UnpostLine(oldIso, ol.Code2, ol.Side, ol.Amount);

                // 2) HAPUS baris lama pakai oldNo (bukan newNo!)
                var delOld = await cn.ExecuteAsync("DELETE FROM JournalLine WHERE NoTran=@no;", new { no = oldNo }, tx);
                DebugLog.Info("SAVE-EDIT", $"DELETE JournalLine oldNo={oldNo} affected={delOld}");

                // (opsional, kalau mau zeroization yang eksplisit)
                await cn.ExecuteAsync(
                    "UPDATE JournalHeader SET TotalDebet=0, TotalKredit=0 WHERE NoTran=@no;",
                    new { no = oldNo }, tx);

                // 3) Kalau NoTran berubah → rename header SETELAH baris lama bersih
                if (!string.Equals(newNo, oldNo, StringComparison.OrdinalIgnoreCase))
                {
                    DebugLog.Info("SAVE-EDIT", $"Rename header: {oldNo} -> {newNo}");
                    var ren = await cn.ExecuteAsync(
                        "UPDATE JournalHeader SET NoTran=@newNo WHERE NoTran=@oldNo;",
                        new { newNo, oldNo }, tx);
                    DebugLog.Info("SAVE-EDIT", $"Rename affected={ren}");
                }

                // 4) Update header (tanggal/type) pakai newNo (sudah rename bila perlu)
                var upHdr = await cn.ExecuteAsync(
                    "UPDATE JournalHeader SET Tanggal=@tgl, Type=@typ WHERE NoTran=@no;",
                    new { tgl = newTanggal.ToString("yyyy-MM-dd"), typ = newType, no = newNo }, tx);

                // 5) Insert lines baru (trigger akan isi TotalDebet/Kredit dari 0)
                const string ins = @"INSERT INTO JournalLine(NoTran,Code2,Side,Amount,Narration)
                     VALUES(@NoTran,@Code2,@Side,@Amount,@Narration)";
                foreach (var nl in newLines)
                {
                    await cn.ExecuteAsync(ins, new
                    {
                        NoTran = newNo,
                        Code2 = nl.Code2,
                        Side = nl.Side,
                        Amount = nl.Amount,
                        Narration = nl.Narration ?? ""
                    }, tx);
                }

                // 6) Re-post ke CoaBalance (tanpa menyentuh Saldo)
                var newIso = newTanggal.ToString("yyyy-MM-dd");
                foreach (var nl in newLines)
                    await posting.PostLine(newIso, nl.Code2, nl.Side, nl.Amount);

                tx.Commit();
                DebugLog.Info("SAVE-EDIT", "COMMIT OK");
            }
        }



        //Buttons
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
            // === Tambah baris saat EDIT ===
            if (_mode == Mode.Edit)
            {
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
                return; // penting: jangan terus ke flow NEW/View
            }

            // ======= kode lama punyamu di bawah ini biarkan apa adanya =======
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
            var dlgNew = new JournalLineDialog { Owner = this };
            if (dlgNew.ShowDialog() == true)
            {
                var line = dlgNew.Result;
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


        // EDIT: di VIEW → toggle header edit? (sesuai flow baru kita pakai utk SAVE saat NEW)
        private async void BtnEdit_Click(object sender, RoutedEventArgs e)
        {
            if (_mode == Mode.New)
            {
                MessageBox.Show("Selesaikan atau batalkan NEW terlebih dahulu.", "Info",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (_mode == Mode.Edit)
            {
                MessageBox.Show("Sedang di mode EDIT.", "Info",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var h = Cur();
            if (h == null)
            {
                MessageBox.Show("Tidak ada jurnal terpilih.", "Info",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Snapshot header & lines lama (untuk unpost dan pembanding rename NoTran)
            _oldHeaderSnapshot = new JournalHeader
            {
                NoTran = h.NoTran,
                Tanggal = h.Tanggal,
                Type = h.Type,
                TotalDebet = h.TotalDebet,
                TotalKredit = h.TotalKredit,
                Memo = h.Memo
            };
            _oldLinesSnapshot = await _lineRepo.ListByNoTran(h.NoTran);

            // Prefill UI header dari h (sudah terjadi via RenderCurrentHeader)
            // Masuk ke mode edit
            EnterEditMode();
        }

        private async void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_mode == Mode.New)
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

                    using (var cn = Db.Open())
                    using (var tx = cn.BeginTransaction())
                    {
                        await cn.ExecuteAsync(
                            "INSERT INTO JournalHeader(NoTran,Tanggal,Type,Memo) VALUES(@NoTran,@Tanggal,@Type,@Memo)",
                            new { _tempHeader.NoTran, _tempHeader.Tanggal, _tempHeader.Type, Memo = (string)null }, tx);

                        foreach (var r in _draft)
                        {
                            await cn.ExecuteAsync(
                                "INSERT INTO JournalLine(NoTran,Code2,Side,Amount,Narration) VALUES(@NoTran,@Code2,@Side,@Amount,@Narration)",
                                new { NoTran = _tempHeader.NoTran, Code2 = r.Code2, Side = r.Side, Amount = r.Amount, Narration = r.Narration ?? "" }, tx);
                        }
                        var posting = new PostingRealtimeService(cn, tx);
                        var newIso = _tempHeader.Tanggal; // sudah ISO yyyy-MM-dd
                        foreach (var r in _draft)
                        {
                            // kalau punya overload 4-arg:
                            await posting.PostLine(newIso, r.Code2, r.Side, r.Amount);

                        
                        }
                        tx.Commit();
                    }

                    var justSavedNo = _tempHeader.NoTran;
                    _headerConfirmed = false; _tempHeader = null; _currentNoTran = null; _draft.Clear();

                    await LoadHeaders(null);
                    var target = _headers.FirstOrDefault(x => x.NoTran == justSavedNo);
                    if (target != null) _view.MoveCurrentTo(target);

                    EnterViewMode();
                    RenderCurrentHeader();
                    MessageBox.Show("Journal tersimpan.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else if (_mode == Mode.Edit)
                {
                    // Ambil input header dari UI untuk edit
                    var (ok, msg, newNo, newType, newTglIso) = ValidateHeaderInputs();
                    if (!ok) { MessageBox.Show(msg, "Validasi", MessageBoxButton.OK, MessageBoxImage.Exclamation); return; }

                    var newTanggal = DateTime.Parse(newTglIso);

                    var (d, k) = CalcTotalsFromDraft();
                    if (d <= 0m || d != k)
                    {
                        MessageBox.Show("Total Debet dan Kredit HARUS balance.", "Validasi",
                            MessageBoxButton.OK, MessageBoxImage.Exclamation);
                        return;
                    }

                    if (_isSaving) return;
                    _isSaving = true;

                    await SaveEditedJournalAsync(
                        _oldHeaderSnapshot,
                        _oldLinesSnapshot,
                        newNo: newNo,
                        newTanggal: newTanggal,
                        newType: newType,
                        newLines: _draft.ToList());

                    // Refresh UI → ke View, posisikan ke newNo
                    await LoadHeaders(null);
                    var target = _headers.FirstOrDefault(x => x.NoTran == newNo);
                    if (target != null) _view.MoveCurrentTo(target);

                    _oldHeaderSnapshot = null;
                    _oldLinesSnapshot = null;
                    _draft.Clear();

                    EnterViewMode();
                    RenderCurrentHeader();
                    MessageBox.Show("Perubahan jurnal tersimpan.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                }
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
            if (_mode == Mode.New)
            {
                MessageBox.Show("Transaksi belum disimpan. Batalkan NEW (Esc) kalau mau keluar.",
                                "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var h = Cur();
            if (h == null) return;

            if (MessageBox.Show($"Hapus SELURUH jurnal '{h.NoTran}' (header + semua baris)?",
                                "Konfirmasi",
                                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;

            try
            {
                using (var cn = Db.Open())
                using (var tx = cn.BeginTransaction())
                {
                    try
                    {
                        // 0) Ambil header & lines dari koneksi/tx yang SAMA
                        var hdr = await cn.QueryFirstOrDefaultAsync<JournalHeader>(
                            "SELECT NoTran,Tanggal,Type,TotalDebet,TotalKredit,Memo FROM JournalHeader WHERE NoTran=@no;",
                            new { no = h.NoTran }, tx);

                        if (hdr == null)
                        {
                            tx.Rollback();
                            MessageBox.Show("Header tidak ditemukan (mungkin sudah terhapus).", "Info",
                                MessageBoxButton.OK, MessageBoxImage.Information);
                            return;
                        }

                        var lines = (await cn.QueryAsync<JournalLineRecord>(@"
SELECT rowid AS Id, NoTran, Code2, Side, Amount, Narration
FROM JournalLine
WHERE NoTran=@no;", new { no = h.NoTran }, tx)).ToList();

                        // 1) Unpost semua baris (pakai tanggal ISO dari header lama)
                        var posting = new PostingRealtimeService(cn, tx);
                        foreach (var ol in lines)
                            await posting.UnpostLine(hdr.Tanggal, ol.Code2, ol.Side, ol.Amount);

                        // 2) Hapus lines lalu header
                        var delLines = await cn.ExecuteAsync("DELETE FROM JournalLine WHERE NoTran=@no;", new { no = h.NoTran }, tx);
                        var delHdr = await cn.ExecuteAsync("DELETE FROM JournalHeader WHERE NoTran=@no;", new { no = h.NoTran }, tx);

                        tx.Commit();
                        DebugLog.Info("DEL", $"Unposted {lines.Count}, deleted header={delHdr}, lines={delLines} for {h.NoTran}");
                    }
                    catch (Exception exInTx)
                    {
                        tx.Rollback();
                        DebugLog.Info("DEL-ERR", exInTx.ToString());
                        throw; // lempar ke outer catch supaya user dapat pesan
                    }
                }

                // Refresh UI
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
                DebugLog.Info("ERROR", ex.ToString());
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

            var dlg = new FindJournalDialogPlaceholder(_hdrRepo, _lineRepo, _coaRepo) { Owner = this };
            if (dlg.ShowDialog() == true)
            {
                var chosen = dlg.SelectedNoTran;
                await LoadHeaders(null);
                var target = _headers.FirstOrDefault(x => x.NoTran == chosen);
                if (target != null) { _view.MoveCurrentTo(target); RenderCurrentHeader(); }
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
            if (_mode == Mode.New)
            {
                BtnSave.IsEnabled = _headerConfirmed && IsBalancedDraft(out _, out _);
                return;
            }
            if (_mode == Mode.Edit)
            {
                BtnSave.IsEnabled = IsBalancedDraft(out _, out _);
                return;
            }
            BtnSave.IsEnabled = false;
        }

        private void UpdateTotalsFromDraft()
        {
            if (!(_mode == Mode.New || _mode == Mode.Edit)) return;
            if (_mode == Mode.New && !_headerConfirmed) return;

            var (d, k) = CalcTotalsFromDraft();

            TxtTotD.Text = d.ToString("N2");
            TxtTotK.Text = k.ToString("N2");

            var diff = d - k;
            var note = diff == 0m ? "BALANCED" : ("UNBALANCED: " + diff.ToString("N2"));
            var modeNote = (_mode == Mode.Edit) ? "EDIT" : (_headerConfirmed ? "NEW(Header OK)" : "NEW");
            UpdateStatus(modeNote + " — " + note);

            UpdateSaveButtonState();

            // === DEBUG ===
            DebugLog.Info("TOTALS", $"Mode={_mode}, D={TxtTotD.Text}, K={TxtTotK.Text}");
        }



        // ===================== Draft totals (live) =====================





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
            var v = ValidateHeaderInputs();
            if (!v.ok) { MessageBox.Show(v.msg, "Validasi", MessageBoxButton.OK, MessageBoxImage.Exclamation); return; }

            var exists = await _hdrRepo.Get(v.no);
            if (exists != null)
            {
                MessageBox.Show("No. Transaksi sudah ada.", "Validasi", MessageBoxButton.OK, MessageBoxImage.Exclamation);
                return;
            }

            _tempHeader = new JournalHeader
            {
                NoTran = v.no,
                Tanggal = v.tglIso,
                Type = v.jenis,
                TotalDebet = 0m,
                TotalKredit = 0m,
                Memo = ""
            };
            _currentNoTran = v.no;
            _headerConfirmed = true;

            SetHeaderEditable(false);
            SetGridReadonly(true);
            if (_draft == null) _draft = new ObservableCollection<LineDraft>();
            _draft.Clear();
            _draft.CollectionChanged += (s2, e2) =>
            {
                if (e2.NewItems != null)
                    foreach (var it in e2.NewItems) HookDraft(it as LineDraft);
            };
            GridLines.ItemsSource = _draft;

            TxtTotD.Text = "0.00";
            TxtTotK.Text = "0.00";
            UpdateStatus($"NEW — Header confirmed: {_currentNoTran}. Tekan Add untuk menambah baris.");
            BtnSave.IsEnabled = false;
            BtnConfirmHeader.IsEnabled = false;

            // === DEBUG ===
            DebugLog.Info("NEW", $"Header confirmed no={_currentNoTran} tgl={_tempHeader.Tanggal} type={_tempHeader.Type}");
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
    public sealed class JournalHeaderRow
    {
        public string NoTran { get; set; }     // e.g. "TRN/05/001"
        public string Tanggal { get; set; }    // ISO yyyy-MM-dd (atau apa adanya dari DB)
        public string Debet { get; set; }      // gabungan "code2 — nama; code2 — nama; ..."
        public string Kredit { get; set; }     // idem
    }
    // =============== PLACEHOLDER untuk Find (opsional) ===============
    public class FindJournalDialogPlaceholder : Window
    {
        private readonly IJournalHeaderRepository _hdrRepo;
        private readonly IJournalLineRepository _lineRepo;
        private readonly ICoaRepository _coaRepo;

        private readonly Dictionary<string, string> _nameCache = new Dictionary<string, string>();
        private DataGrid _dg;

        public string SelectedNoTran { get; private set; }

        public FindJournalDialogPlaceholder(
            IJournalHeaderRepository hdrRepo,
            IJournalLineRepository lineRepo,
            ICoaRepository coaRepo)
        {
            _hdrRepo = hdrRepo;
            _lineRepo = lineRepo;
            _coaRepo = coaRepo;

            Title = "Find Journal";
            Width = 900; Height = 480;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = System.Windows.Media.Brushes.Black;

            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            _dg = new DataGrid
            {
                AutoGenerateColumns = false,
                IsReadOnly = true,
                Background = System.Windows.Media.Brushes.White,   // latar putih
                Foreground = System.Windows.Media.Brushes.Black,   // teks hitam
                BorderThickness = new Thickness(0),
                GridLinesVisibility = DataGridGridLinesVisibility.None
            };

            _dg.ColumnHeaderStyle = new Style(typeof(DataGridColumnHeader))
            {
                Setters =
                {
                    new Setter(Control.BackgroundProperty, Brushes.LightGray),
                    new Setter(Control.ForegroundProperty, Brushes.Black),
                    new Setter(Control.FontWeightProperty, FontWeights.Bold)
                }
            };

            _dg.Columns.Add(new DataGridTextColumn
            { Header = "No. Transaksi", Binding = new Binding(nameof(JournalHeaderRow.NoTran)), Width = 180 });

            _dg.Columns.Add(new DataGridTextColumn
            { Header = "Tanggal", Binding = new Binding(nameof(JournalHeaderRow.Tanggal)), Width = 120 });

            _dg.Columns.Add(new DataGridTextColumn
            { Header = "Debet (D)", Binding = new Binding(nameof(JournalHeaderRow.Debet)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });

            _dg.Columns.Add(new DataGridTextColumn
            { Header = "Kredit (K)", Binding = new Binding(nameof(JournalHeaderRow.Kredit)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });

            _dg.MouseDoubleClick += (s, e) =>
            {
                if (_dg.SelectedItem is JournalHeaderRow it)
                {
                    SelectedNoTran = it.NoTran;
                    DialogResult = true;
                }
            };
            _dg.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter && _dg.SelectedItem is JournalHeaderRow it)
                {
                    SelectedNoTran = it.NoTran;
                    DialogResult = true;
                    e.Handled = true;
                }
            };

            var close = new Button { Content = "Close", Margin = new Thickness(8) };
            close.Click += (s, e) => { DialogResult = false; };

            Grid.SetRow(_dg, 0);
            Grid.SetRow(close, 1);
            root.Children.Add(_dg);
            root.Children.Add(close);
            Content = root;
        }

        protected override async void OnContentRendered(EventArgs e)
        {
            base.OnContentRendered(e);

            // 1) Ambil semua header
            var headers = await _hdrRepo.All();

            // 2) Untuk tiap header, ambil semua line & susun Debet/Kredit
            var buildTasks = headers.Select(async h =>
            {
                var lines = await _lineRepo.ListByNoTran(h.NoTran);

                // Pisah D dan K
                var d = new List<string>();
                var k = new List<string>();

                foreach (var r in lines)
                {
                    var entry = await FormatCode2WithName(r.Code2);
                    if (string.Equals(r.Side, "D", StringComparison.OrdinalIgnoreCase))
                        d.Add(entry);
                    else
                        k.Add(entry);
                }

                return new JournalHeaderRow
                {
                    NoTran = h.NoTran,
                    Tanggal = h.Tanggal,                 // tampilkan apa adanya (ISO yyyy-MM-dd)
                    Debet = string.Join("; ", d),        // "100.200 — Kas; 110.001 — Piutang"
                    Kredit = string.Join("; ", k)
                };
            });

            var rows = await Task.WhenAll(buildTasks);
            _dg.ItemsSource = rows.OrderBy(x => x.NoTran).ToList();
            if (rows.Length > 0) _dg.SelectedIndex = 0;
        }

        private async Task<string> FormatCode2WithName(string code2)
        {
            if (string.IsNullOrWhiteSpace(code2)) return code2 ?? "";
            if (_nameCache.TryGetValue(code2, out var nm)) return $"{code2} — {nm}";

            var coa = await _coaRepo.Get(code2 + ".001");
            nm = (coa?.Name ?? "");
            _nameCache[code2] = nm;
            return string.IsNullOrEmpty(nm) ? code2 : $"{code2} — {nm}";
        }
    }
}
