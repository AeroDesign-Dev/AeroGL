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
        private void OpenSelection()
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

            // Menu E (Ubah Password):
            // - Kalau belum diset: langsung buka (first-time setup)
            // - Kalau sudah diset: minta password dulu
            if (key == 'E')
            {
                if (IsUtilityPasswordSet())
                {
                    if (!PromptAndVerifyUtilityPassword()) return;
                }
                var w = new ChangePasswordWindow { Owner = this };
                w.ShowDialog();
                return;
            }

            // Buka menu sesuai pilihan
            switch (key)
            {
                case 'A':
                    var w = new EntryKodeProyekWindow { Owner = this };
                    w.ShowDialog();
                    break;
                case 'B':
                    MessageBox.Show("B - Entry Tabel\n\n(placeholder)", "AeroGL",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    break;
                case 'C':
                    MessageBox.Show("C - Proses Akhir Bulan\n\n(placeholder)", "AeroGL",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    break;
                case 'D':
                    MessageBox.Show("D - Proses Akhir Tahun\n\n(placeholder)", "AeroGL",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    break;
                case 'F':
                    MessageBox.Show("F - Reposting Data\n\n(placeholder)", "AeroGL",
                        MessageBoxButton.OK, MessageBoxImage.Information);
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
