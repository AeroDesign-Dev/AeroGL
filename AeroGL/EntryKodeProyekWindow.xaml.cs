using AeroGL.Core;
using AeroGL.Data;
using System;
using System.Windows;

namespace AeroGL
{
    public partial class EntryKodeProyekWindow : Window
    {
        // Pake repo master buat update record di master.db
        private readonly ICompanyRepository _masterRepo = new MasterRepository();

        public EntryKodeProyekWindow()
        {
            InitializeComponent();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // Ambil data PT yang sedang aktif saat ini
            var current = CurrentCompany.Data;

            if (current == null) { Close(); return; }

            // Kode Proyek kita bikin ReadOnly karena ini identitas unik di Master
            TxtKode.Text = "001"; // Default atau lu bisa tambah field Code di Company POCO nanti
            TxtKode.IsReadOnly = true;

            TxtNama.Text = current.Name;
            TxtPass.Password = current.Password;

            TxtStatus.Text = $"Aktif: {current.Name}";
            TxtNama.Focus();
        }

        private async void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            var newName = (TxtNama.Text ?? "").Trim();
            var newPass = TxtPass.Password ?? "";

            if (string.IsNullOrEmpty(newName))
            {
                MessageBox.Show("Nama proyek wajib diisi.", "AeroGL", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                this.Cursor = System.Windows.Input.Cursors.Wait;

                // 1. Update data di memori CurrentCompany
                CurrentCompany.Data.Name = newName;
                CurrentCompany.Data.Password = newPass;

                // 2. Persist ke master.db (Biar di ProjectGateWindow datanya berubah)
                await _masterRepo.Upsert(CurrentCompany.Data);

                // 3. Update identitas di dalam Database Akuntansi lokal (Tabel Config)
                // Ini penting supaya Header Laporan Neraca/Rugi Laba ikut berubah
                AccountConfig.Set("CompanyName", newName);

                MessageBox.Show("Identitas Proyek berhasil diperbarui.", "Sukses",
                    MessageBoxButton.OK, MessageBoxImage.Information);

                DialogResult = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Gagal update: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                this.Cursor = System.Windows.Input.Cursors.Arrow;
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

        private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter) BtnSave_Click(sender, e);
            if (e.Key == System.Windows.Input.Key.Escape) Close();
        }
    }
}