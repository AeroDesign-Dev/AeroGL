using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace AeroGL
{
    public partial class MainWindow : Window
    {
        private readonly List<MenuEntry> _items = new List<MenuEntry>
        {
            new MenuEntry('A', "Entry Kode Rekening", "\uE70F"),    
            new MenuEntry('B', "Entry Jurnal Harian", "\uE70B"),    
            new MenuEntry('C', "Print Jurnal Umum", "\uE749"),      
            new MenuEntry('D', "Print Per Rekening", "\uE81E"),     
            new MenuEntry('E', "Print Neraca Percobaan", "\uE9D2"),
            new MenuEntry('F', "Print Neraca & Rugi/Laba", "\uE9F5"),
            new MenuEntry('G', "Print Rugi/Laba", "\uE9D9"),
            new MenuEntry('H', "Print Neraca", "\uE9BE"),
            new MenuEntry('I', "Print Perincian", "\uE8A5"),
            new MenuEntry('J', "Utility", "\uE90F"),
            new MenuEntry('X', "Exit", "\uE8BB"),
        };

        public MainWindow()
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
            // Pastikan yang diklik adalah item (bukan scrollbar / area kosong)
            var item = FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject);
            if (item != null)
            {
                // Sinkronkan selection (kalau belum) lalu buka
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

        // helper cari ancestor tipe tertentu di visual tree
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

        private void OpenSelection()
        {
            var m = MenuList.SelectedItem as MenuEntry;
            if (m == null) return;

            var key = char.ToUpperInvariant(m.Hotkey);
            if (key == 'X') { Close(); return; }

            // 1. Gate Perusahaan: Panggil gate untuk SEMUA menu kecuali Utility (J) dan Exit (X)
            if (key != 'J' && key != 'X')
            {
                // Jika user cancel di gate atau database gagal dimuat, jangan lanjut
                if (!ShowCompanyGate()) return;
            }

            // 2. Buka Window Target
            // Tidak perlu kirim projCode lagi karena Repository sudah otomatis baca CurrentCompany.Data
            if (key == 'A') { new CoaWindow { Owner = this }.ShowDialog(); return; }
            if (key == 'B') { new JournalWindow { Owner = this }.ShowDialog(); return; }
            if (key == 'C') { new ReportJurnalUmumWindow { Owner = this }.ShowDialog(); return; }
            if (key == 'D') { new ReportPerRekeningWindow { Owner = this }.ShowDialog(); return; }
            if (key == 'E') { new ReportNeracaPercobaanWindow { Owner = this }.ShowDialog(); return; }
            if (key == 'F') { new ReportNeracaLajurWindow { Owner = this }.ShowDialog(); return; }
            if (key == 'G') { new ReportRugiLabaWindow { Owner = this }.ShowDialog(); return; }
            if (key == 'H') { new ReportNeracaWindow { Owner = this }.ShowDialog(); return; }
            if (key == 'I') { new ReportPerincianWindow { Owner = this }.ShowDialog(); return; }

            if (key == 'J')
            {
                new UtilityWindow { Owner = this }.ShowDialog();
                return;
            }

            MessageBox.Show(m.Hotkey + " - " + m.Title + "\n\n(placeholder)", "AeroGL");
        }


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
        private bool ShowCompanyGate()
        {
            // Cek apakah context perusahaan sudah terisi (dari login awal App.xaml.cs)
            // Jika belum atau mau ganti PT, tampilkan gate
            if (AeroGL.Core.CurrentCompany.IsLoaded) return true;

            var dlg = new ProjectGateWindow { Owner = this };
            return dlg.ShowDialog() == true;
        }


    }

    public sealed class MenuEntry
    {
        public char Hotkey { get; private set; }
        public string Title { get; private set; }
        public string Icon { get; private set; } // Segoe MDL2 glyph

        public MenuEntry(char hotkey, string title, string icon)
        {
            Hotkey = hotkey;
            Title = title;
            Icon = icon;
        }
    }




}
