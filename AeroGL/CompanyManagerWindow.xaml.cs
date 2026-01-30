using AeroGL.Core;
using AeroGL.Data;
using Microsoft.Win32;
using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

// Alias untuk folder browser
using WinForms = System.Windows.Forms;

namespace AeroGL
{
    public partial class CompanyManagerWindow : Window
    {
        private readonly ICompanyRepository _repo = new MasterRepository();

        public CompanyManagerWindow()
        {
            InitializeComponent();
            LoadData();

            TxtName.TextChanged += (s, e) => SetButtonStates();
        }

        private async void LoadData()
        {
            GridCompany.ItemsSource = await _repo.GetAll();
        }

        private void SetButtonStates()
        {
            var name = TxtName.Text.Trim();
            var path = TxtPath.Text.Trim();
            bool fileExists = !string.IsNullOrEmpty(path) && File.Exists(path);
            bool hasSelection = GridCompany.SelectedItem != null;
            bool hasNameAndPath = !string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(path);

            // 1. DAFTARKAN: Aktif jika Nama diisi, Path ada, dan Filenya MEMANG ADA di disk.
            BtnDaftarkan.IsEnabled = hasNameAndPath && fileExists;

            // 2. BUAT DB BARU: Aktif jika Nama diisi, Path ada, dan Filenya BELUM ADA (baru mau dibuat).
            BtnBuatBaru.IsEnabled = hasNameAndPath && !fileExists;

            // 3. HAPUS & IMPORT: Hanya aktif jika ada baris yang dipilih di tabel.
            BtnHapus.IsEnabled = hasSelection;
            BtnImport.IsEnabled = hasSelection;
        }

        private void GridCompany_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            SetButtonStates();
        }

        private void Browse_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "SQLite Database|*.db",
                Title = "Pilih atau Tentukan Lokasi Database",
                // KUNCI: Supaya user bisa ngetik nama file baru yang belum ada di disk
                CheckFileExists = false,
                CheckPathExists = true
            };

            if (dialog.ShowDialog() == true)
            {
                TxtPath.Text = dialog.FileName;

                // Logic Info (Visual Feedback)
                if (File.Exists(dialog.FileName))
                {
                    TxtInfo.Text = "Database ditemukan. Klik 'DAFTARKAN'.";
                    TxtInfo.Foreground = System.Windows.Media.Brushes.LightGreen;
                }
                else
                {
                    // Sekarang ini bakal muncul kalau user ngetik nama baru
                    TxtInfo.Text = "File tidak ada. Klik 'BUAT DB BARU'.";
                    TxtInfo.Foreground = System.Windows.Media.Brushes.Yellow;
                }

                SetButtonStates();
            }
        }

        // UNTUK REGISTER FILE .DB YANG SUDAH ADA (aerogl.db)
        private async void RegisterExisting_Click(object sender, RoutedEventArgs e)
        {
            var name = TxtName.Text.Trim();
            var path = TxtPath.Text.Trim();

            if (!File.Exists(path))
            {
                MessageBox.Show("File database tidak ditemukan di folder tersebut!");
                return;
            }

            await SaveToMaster(name, path, TxtPassword.Password);
            MessageBox.Show("Perusahaan lama berhasil disambungkan.");
            LoadData();
        }

        // UNTUK BUAT PT BARU DARI NOL
        private async void CreateNew_Click(object sender, RoutedEventArgs e)
        {
            var name = TxtName.Text.Trim();
            var path = TxtPath.Text.Trim();

            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(path))
            {
                MessageBox.Show("Isi Nama PT dan Path dulu!"); return;
            }

            try
            {
                if (File.Exists(path))
                {
                    MessageBox.Show("File sudah ada! Gunakan tombol 'DAFTARKAN' saja.");
                    return;
                }

                TxtInfo.Text = "Creating database...";
                // Menjalankan inisialisasi di background agar UI tidak freeze
                await Task.Run(() => DbInitializer.CreateNewDatabase(path, name));

                await SaveToMaster(name, path, TxtPassword.Password);
                MessageBox.Show("Database baru berhasil diciptakan.");
                LoadData();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Gagal buat database: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally { TxtInfo.Text = "Ready."; }
        }

        private async Task SaveToMaster(string name, string path, string pass)
        {
            var company = new Company
            {
                Id = Guid.NewGuid(),
                Name = name,
                DbPath = path,
                Password = pass,
                CreatedDate = DateTime.Now
            };
            await _repo.Upsert(company);
        }

        private async void Delete_Click(object sender, RoutedEventArgs e)
        {
            if (GridCompany.SelectedItem is Company selected)
            {
                if (MessageBox.Show($"Hapus pendaftaran {selected.Name}?", "Konfirmasi", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                {
                    await _repo.Delete(selected.Id);
                    LoadData();
                }
            }
        }

        private async void ImportDbf_Click(object sender, RoutedEventArgs e)
        {
            if (!(GridCompany.SelectedItem is Company selected))
            {
                MessageBox.Show("Pilih PT target di tabel dulu!"); return;
            }

            var dialog = new WinForms.FolderBrowserDialog { Description = "Pilih Folder berisi DBF" };
            if (dialog.ShowDialog() == WinForms.DialogResult.OK)
            {
                try
                {
                    CurrentCompany.Data = selected;
                    TxtInfo.Text = "Importing... Sabar bro.";

                    await DbfImporter.RunOnce(dialog.SelectedPath);

                    MessageBox.Show("Data DBF berhasil masuk ke " + selected.Name);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Error pas import: " + ex.Message);
                }
                finally
                {
                    TxtInfo.Text = "Ready.";
                }
            }
        }
    }
}