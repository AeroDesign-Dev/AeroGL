using AeroGL.Core; //
using AeroGL.Data; //
using System;
using System.Threading.Tasks;
using System.Windows;

namespace AeroGL
{
    public partial class ProjectGateWindow : Window
    {
        // Panggil Repository Master untuk mengambil daftar PT
        private readonly ICompanyRepository _repo = new MasterRepository();

        public ProjectGateWindow()
        {
            InitializeComponent();

            _ = RefreshData();
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            var selected = ComboPerusahaan.SelectedItem as Company;
            if (selected == null)
            {
                MessageBox.Show("Pilih perusahaan dulu!");
                return;
            }

            CurrentCompany.Data = selected;
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {

        }

        // Buat fungsi khusus refresh agar bisa dipanggil berkali-kali
        private async Task RefreshData()
        {
            try
            {
                var companies = await _repo.GetAll();
                ComboPerusahaan.ItemsSource = companies;

                if (companies.Count > 0 && ComboPerusahaan.SelectedIndex == -1)
                    ComboPerusahaan.SelectedIndex = 0;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Gagal memuat daftar perusahaan: " + ex.Message, "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void ManageCompany_Click(object sender, RoutedEventArgs e)
        {
            var manager = new CompanyManagerWindow();
            manager.Owner = this;
            manager.ShowDialog();

            await RefreshData();
        }
        
    }

}