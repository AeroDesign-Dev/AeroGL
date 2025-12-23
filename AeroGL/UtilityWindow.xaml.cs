using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

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
            if ("ABCDF".IndexOf(key) >= 0)
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

                    // 1. Tentukan mau close bulan apa.
                    // IDEALNYA: Lo bikin Window kecil (DatePicker) buat user pilih bulan.
                    // TAPI BUAT SEKARANG: Kita default ke "Bulan Lalu" (Common practice).
                    var today = DateTime.Now;
                    int targetMonth = today.Month - 1;
                    int targetYear = today.Year;

                    // Handle ganti tahun (Kalau sekarang Januari, close Desember tahun lalu)
                    if (targetMonth == 0)
                    {
                        targetMonth = 12;
                        targetYear--;
                    }

                    // 2. Validasi (Biar gak nabrak aturan Month 12 harus Year End)
                    if (targetMonth == 12)
                    {
                        MessageBox.Show("Bulan 12 adalah Tutup Tahun.\nGunakan menu 'D' (Proses Akhir Tahun).",
                            "Salah Menu", MessageBoxButton.OK, MessageBoxImage.Warning);
                        break;
                    }

                    // 3. Konfirmasi ke User
                    var confirmClose = MessageBox.Show(
                        $"Yakin ingin melakukan PROSES AKHIR BULAN untuk periode: {targetMonth}/{targetYear}?\n\n" +
                        "Proses ini akan:\n" +
                        "1. Mengunci transaksi bulan tersebut.\n" +
                        "2. Memindahkan saldo akhir ke saldo awal bulan depan.\n" +
                        "3. Me-reset Pendapatan & Biaya jadi 0 (Sesuai mode DOS).\n\n" +
                        "Lanjutkan?",
                        "Konfirmasi Closing",
                        MessageBoxButton.YesNo, MessageBoxImage.Question);

                    if (confirmClose == MessageBoxResult.Yes)
                    {
                        try
                        {
                            // UI Feedback biar user tau lagi loading
                            this.Cursor = Cursors.Wait;
                            TxtDate.Text = "Processing Closing...";

                            // --- CALL SERVICE YANG KITA BUAT TADI ---
                            var closeSvc = new AeroGL.Data.MonthlyClosingService();
                            await closeSvc.RunClosing(targetYear, targetMonth);

                            MessageBox.Show("Sukses! Proses Akhir Bulan selesai.", "AeroGL", MessageBoxButton.OK, MessageBoxImage.Information);
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show($"Gagal Closing: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        }
                        finally
                        {
                            // Balikin UI ke semula
                            this.Cursor = Cursors.Arrow;
                            TxtDate.Text = DateTime.Now.ToString("dd-MM-yyyy");
                        }
                    }
                    break;
                
                case 'D': // --- PROSES AKHIR TAHUN (Year End - Opsi A: DOS Style) ---

                    // Asumsi: User menjalankan ini untuk menutup tahun berjalan.
                    // Kalau mau aman, bisa cek bulan. Kalau bulan 1, mungkin mau tutup tahun lalu (Year-1).
                    // Tapi default-nya kita set Tahun Sekarang.
                    int closingYear = DateTime.Now.Year;

                    // Warning Message yang JUJUR sesuai Opsi A
                    var confirmYear = MessageBox.Show(
                        $"PERINGATAN: ANDA AKAN MELAKUKAN TUTUP BUKU TAHUN {closingYear}.\n\n" +
                        "Proses ini akan:\n" +
                        "1. Me-RESET semua akun Pendapatan & Biaya menjadi 0.\n" +
                        "2. Membentuk Saldo Awal tahun depan.\n" +
                        "3. TIDAK menjurnal otomatis Laba ke Modal (Harus Jurnal Manual nanti).\n\n" +
                        "Pastikan semua transaksi bulan 1-12 sudah selesai.\n" +
                        "LANJUTKAN?",
                        "Konfirmasi Year End",
                        MessageBoxButton.YesNo, MessageBoxImage.Warning);

                    if (confirmYear == MessageBoxResult.Yes)
                    {
                        try
                        {
                            // UI Feedback
                            this.Cursor = Cursors.Wait;
                            TxtDate.Text = "Proses Year End...";

                            // Panggil Service Opsi A yang tadi kita buat
                            var yeSvc = new AeroGL.Data.YearEndClosingService();
                            await yeSvc.RunYearEnd(closingYear);

                            MessageBox.Show($"Tutup Buku Tahun {closingYear} SELESAI.\n" +
                                $"Saldo Awal Tahun {closingYear + 1} sudah terbentuk.\n" +
                                "Silakan cek Neraca Awal dan lakukan penyesuaian manual jika diperlukan.",
                                "Sukses", MessageBoxButton.OK, MessageBoxImage.Information);
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show($"Gagal Year End: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        }
                        finally
                        {
                            // Reset UI
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
                    var year = DateTime.Now.Year; // Bisa diganti prompt pilih tahun kalau mau advanced

                    var res = MessageBox.Show(
                        $"Yakin mau Reposting Data tahun {year}?\n\n" +
                        "Proses ini akan:\n" +
                        "1. Mereset data saldo Debet/Kredit.\n" +
                        "2. Menghitung ulang semua Jurnal dari awal.\n" +
                        "3. Memperbaiki Saldo Akhir yang tidak balance.\n\n" +
                        "Lanjutkan?",
                        "Konfirmasi Reposting",
                        MessageBoxButton.YesNo, MessageBoxImage.Warning);

                    if (res == MessageBoxResult.Yes)
                    {
                        try
                        {
                            this.Cursor = Cursors.Wait;
                            TxtDate.Text = "Sedang Memproses..."; // Status bar update

                            var svc = new AeroGL.Data.RepostingService();

                            // Update text realtime dari service
                            svc.OnProgress += (msg) => { TxtDate.Text = msg; };

                            await svc.RunReposting(year);

                            TxtDate.Text = DateTime.Now.ToString("dd-MM-yyyy"); // Balikin tanggal
                            MessageBox.Show("Sukses! Proses Reposting Selesai.", "AeroGL", MessageBoxButton.OK, MessageBoxImage.Information);
                        }
                        catch (Exception ex)
                        {
                            TxtDate.Text = "Error!";
                            MessageBox.Show($"Gagal Reposting: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        }
                        finally
                        {
                            this.Cursor = Cursors.Arrow;
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
            // Hanya boleh dipanggil saat password SUDAH diset
            var dlg = new PasswordPromptWindow { Owner = this };
            if (dlg.ShowDialog() == true)
            {
                var stored = global::AeroGL.Properties.Settings.Default.UtilityPassword ?? "";
                if (dlg.EnteredPassword == stored) return true;

                MessageBox.Show("Password salah!", "AeroGL",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            return false;
        }

        private static bool IsUtilityPasswordSet()
        {
            var stored = global::AeroGL.Properties.Settings.Default.UtilityPassword;
            return !string.IsNullOrEmpty(stored);
        }
    }

}
