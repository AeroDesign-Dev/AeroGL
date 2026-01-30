using AeroGL.Core;
using AeroGL.Data;
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;

namespace AeroGL
{
    public partial class UtilityWindow : Window
    {
        // NOTE: memakai MenuEntry yang sudah ada di project lu
        private readonly List<MenuEntry> _items = new List<MenuEntry>
        {
            new MenuEntry('A', "Entry Kode Proyek",     "\uE70F"),
            new MenuEntry('B', "Entry Tabel",           "\uE8B7"),
            new MenuEntry('C', "Proses Akhir Bulan",    "\uE823"),
            new MenuEntry('D', "Proses Akhir Tahun",    "\uE823"),
            new MenuEntry('E', "Ubah Password",         "\uE72E"),
            new MenuEntry('F', "Reposting Data",        "\uE7C3"),
            new MenuEntry('G', "Backup Database Manual","\uE8B5"),
            new MenuEntry('X', "Exit",                  "\uE8BB"),
        };

        public UtilityWindow()
        {
            InitializeComponent();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            TxtDate.Text = DateTime.Now.ToString("dd-MM-yyyy");
            MenuList.ItemsSource = _items;
            MenuList.SelectedIndex = 0;
            MenuList.Focus();
        }

        private void MenuList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            var item = FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject);
            if (item != null)
            {
                if (!item.IsSelected) item.IsSelected = true;
                OpenSelection();
            }
        }

        private void MenuList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            var item = FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject);
            if (item != null)
            {
                if (!item.IsSelected) item.IsSelected = true;
                OpenSelection();
            }
        }

        private static T FindAncestor<T>(DependencyObject current) where T : DependencyObject
        {
            while (current != null)
            {
                if (current is T match) return match;
                current = VisualTreeHelper.GetParent(current);
            }
            return null;
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape) { Close(); return; }
            if (e.Key == Key.Enter) { OpenSelection(); return; }

            char ch = KeyToChar(e.Key);
            if (ch == '\0') return;

            int idx = _items.FindIndex(x => char.ToUpperInvariant(x.Hotkey) == ch);
            if (idx >= 0)
            {
                MenuList.SelectedIndex = idx;
                if (ch == 'X') Close();
            }
        }
        private bool CheckPassword()
        {
            var dlg = new PasswordPromptWindow { Owner = this };
            if (dlg.ShowDialog() == true)
            {
                var stored = Properties.Settings.Default.UtilityPassword ?? "SINAR";
                if (dlg.EnteredPassword == stored) return true;

                MessageBox.Show("Password salah!", "AeroGL",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            return false;
        }
        private async void OpenSelection()
        {
            var m = MenuList.SelectedItem as MenuEntry;
            if (m == null) return;

            char key = char.ToUpperInvariant(m.Hotkey);
            if (key == 'X') { Close(); return; }

            // Gate A/B/C/D/F – wajib password
            if ("ABCDFG".IndexOf(key) >= 0)
            {
                if (!IsUtilityPasswordSet())
                {
                    MessageBox.Show(
                        "Password Utility belum diset.\nSilakan set terlebih dahulu di menu E (Ubah Password).",
                        "AeroGL", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                if (!PromptAndVerifyUtilityPassword()) return;
            }

            switch (key)
            {
                case 'A':
                    var w = new EntryKodeProyekWindow { Owner = this };
                    w.ShowDialog();
                    break;

                case 'B':
                    // Membuka Window Entry Tabel yang baru kita buat
                    var wTabel = new EntryTabelWindow { Owner = this };
                    wTabel.ShowDialog();
                    break;

                case 'C': // --- PROSES AKHIR BULAN (Monthly Closing) ---
                          // 1. Tampilkan Dialog Input Periode
                    var monthYearDlg = new MonthYearPromptWindow { Owner = this };
                    if (monthYearDlg.ShowDialog() != true) return;

                    int targetMonth = monthYearDlg.SelectedMonth;
                    int targetYear = monthYearDlg.SelectedYear;

                    // 2. Konfirmasi Detail (Sesuai Logika Blackbox)
                    var confirmClose = MessageBox.Show(
                        $"Yakin ingin melakukan PROSES AKHIR BULAN periode {targetMonth}/{targetYear}?\n\n" +
                        "Sistem akan melakukan:\n" +
                        "1. Menghitung Laba Bersih bulan tersebut.\n" +
                        "2. Memindahkan Laba ke akun 'Laba Berjalan' (Modal).\n" +
                        "3. Membentuk Saldo Awal bulan depan.\n" +
                        "4. Me-reset Pendapatan & Biaya bulan depan jadi 0.\n\n" +
                        "Lanjutkan?",
                        "Konfirmasi Closing",
                        MessageBoxButton.YesNo, MessageBoxImage.Question);

                    if (confirmClose == MessageBoxResult.Yes)
                    {
                        try
                        {
                            this.Cursor = Cursors.Wait;
                            TxtDate.Text = $"Closing {targetMonth}/{targetYear}...";

                            // Panggil service yang sudah diupdate dengan logic Laba Berjalan
                            var closeSvc = new AeroGL.Data.MonthlyClosingService();
                            await closeSvc.RunClosing(targetYear, targetMonth);

                            MessageBox.Show($"Sukses! Proses Akhir Bulan {targetMonth}/{targetYear} selesai.",
                                "AeroGL", MessageBoxButton.OK, MessageBoxImage.Information);
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show($"Gagal Closing: {ex.Message}", "Error",
                                MessageBoxButton.OK, MessageBoxImage.Error);
                        }
                        finally
                        {
                            this.Cursor = Cursors.Arrow;
                            TxtDate.Text = DateTime.Now.ToString("dd-MM-yyyy");
                        }
                    }
                    break;

                case 'D': // --- PROSES AKHIR TAHUN (Year End) ---
                          // 1. Panggil Box Tahun (Sama seperti Reposting)
                    var yearDlg1 = new YearPromptWindow { Owner = this };
                    if (yearDlg1.ShowDialog() != true) return;

                    int closingYear = yearDlg1.SelectedYear;

                    // 2. Warning Message yang SPESIFIK ke Tahun Pilihan
                    var confirmYear = MessageBox.Show(
                        $"PERINGATAN: ANDA AKAN MELAKUKAN TUTUP BUKU TAHUN {closingYear}.\n\n" +
                        "Proses ini akan:\n" +
                        "1. Memindahkan Laba Berjalan (017) ke Laba Ditahan (016) tahun depan.\n" +
                        "2. Me-RESET semua akun Pendapatan & Biaya menjadi 0.\n" +
                        "3. Membentuk Saldo Awal (Month 0) untuk tahun {closingYear + 1}.\n\n" +
                        "Pastikan transaksi bulan 1-12 sudah benar.\n" +
                        "LANJUTKAN?",
                        "Konfirmasi Year End",
                        MessageBoxButton.YesNo, MessageBoxImage.Warning);

                    if (confirmYear == MessageBoxResult.Yes)
                    {
                        try
                        {
                            this.Cursor = Cursors.Wait;
                            TxtDate.Text = $"Closing Year {closingYear}...";

                            var yeSvc = new AeroGL.Data.YearEndClosingService();
                            await yeSvc.RunYearEnd(closingYear);

                            MessageBox.Show($"Tutup Buku Tahun {closingYear} SELESAI.\n" +
                                $"Saldo Awal Tahun {closingYear + 1} sudah terbentuk.",
                                "Sukses", MessageBoxButton.OK, MessageBoxImage.Information);
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show($"Gagal Year End: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        }
                        finally
                        {
                            this.Cursor = Cursors.Arrow;
                            TxtDate.Text = DateTime.Now.ToString("dd-MM-yyyy");
                        }
                    }
                    break;

                case 'E':
                    // Menu E (Ubah Password) - logic check passwordnya unik dikit
                    if (IsUtilityPasswordSet())
                    {
                        if (!PromptAndVerifyUtilityPassword()) return;
                    }
                    var wp = new ChangePasswordWindow { Owner = this };
                    wp.ShowDialog();
                    break;

                case 'F': // --- FITUR REPOSTING DATA ---
                          // 1. Panggil Box Tahun
                    var yearDlg = new YearPromptWindow { Owner = this };
                    if (yearDlg.ShowDialog() != true) return;

                    int year = yearDlg.SelectedYear;

                    // 2. Konfirmasi dengan Tahun yang dipilih
                    var res = MessageBox.Show(
                        $"PERINGATAN: Anda akan melakukan Reposting Data tahun {year}.\n\n" +
                        "Proses ini akan:\n" +
                        "1. Mereset data saldo Debet/Kredit pada tahun tersebut.\n" +
                        "2. Menghitung ulang semua Jurnal dari awal berdasarkan tanggal.\n" +
                        "3. Memasukkan kembali mutasi ke CoaBalance.\n\n" +
                        "Lanjutkan?",
                        "Konfirmasi Reposting",
                        MessageBoxButton.YesNo, MessageBoxImage.Warning);

                    if (res == MessageBoxResult.Yes)
                    {
                        try
                        {
                            this.Cursor = Cursors.Wait;
                            TxtDate.Text = "Menghitung...";

                            var svc = new AeroGL.Data.RepostingService();

                            // Wiring event OnProgress agar status bar UtilityWindow terupdate
                            svc.OnProgress += (msg) => {
                                Dispatcher.Invoke(() => TxtDate.Text = msg);
                            };

                            await svc.RunReposting(year); // Menjalankan reposting berdasarkan tahun input

                            MessageBox.Show($"Sukses! Reposting Tahun {year} Selesai.", "AeroGL",
                                MessageBoxButton.OK, MessageBoxImage.Information);
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show($"Gagal Reposting: {ex.Message}", "Error",
                                MessageBoxButton.OK, MessageBoxImage.Error);
                        }
                        finally
                        {
                            this.Cursor = Cursors.Arrow;
                            TxtDate.Text = DateTime.Now.ToString("dd-MM-yyyy");
                        }
                    }
                    break;

                case 'G': // --- LOGIKA BACKUP ---
                    var sfd = new SaveFileDialog
                    {
                        FileName = $"Backup_{CurrentCompany.Data.Name}_{DateTime.Now:yyyyMMdd}.db",
                        Filter = "SQLite Database|*.db"
                    };

                    if (sfd.ShowDialog() == true)
                    {
                        try
                        {
                            var bSvc = new BackupService();
                            bSvc.CopyDatabase(CurrentCompany.Data.DbPath, sfd.FileName);
                            MessageBox.Show("Backup Berhasil!");
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show("Gagal: " + ex.Message);
                        }
                    }
                    break;

                default:
                    MessageBox.Show($"Fitur {key} belum tersedia.", "AeroGL", MessageBoxButton.OK, MessageBoxImage.Information);
                    break;
            }
        }

        // Ganti switch expression -> switch statement klasik
        private static char KeyToChar(Key key)
        {
            switch (key)
            {
                case Key.A: return 'A';
                case Key.B: return 'B';
                case Key.C: return 'C';
                case Key.D: return 'D';
                case Key.E: return 'E';
                case Key.F: return 'F';
                case Key.G: return 'G';
                case Key.H: return 'H';
                case Key.I: return 'I';
                case Key.J: return 'J';
                case Key.K: return 'K';
                case Key.L: return 'L';
                case Key.M: return 'M';
                case Key.N: return 'N';
                case Key.O: return 'O';
                case Key.P: return 'P';
                case Key.Q: return 'Q';
                case Key.R: return 'R';
                case Key.S: return 'S';
                case Key.T: return 'T';
                case Key.U: return 'U';
                case Key.V: return 'V';
                case Key.W: return 'W';
                case Key.X: return 'X';
                case Key.Y: return 'Y';
                case Key.Z: return 'Z';
                default: return '\0';
            }
        }
        private bool PromptAndVerifyUtilityPassword()
        {
            var dlg = new PasswordPromptWindow { Owner = this };
            if (dlg.ShowDialog() == true)
            {
                var stored = AccountConfig.Get("UtilityPassword");
                if (dlg.EnteredPassword == stored) return true;

                MessageBox.Show("Password salah!", "AeroGL", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            return false;
        }

        private static bool IsUtilityPasswordSet()
        {
            return !string.IsNullOrEmpty(AccountConfig.Get("UtilityPassword"));
        }
    }

}
