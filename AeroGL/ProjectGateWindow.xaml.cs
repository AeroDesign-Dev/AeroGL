using AeroGL.Core; //
using AeroGL.Data; //
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
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // 1. Ambil daftar PT dari master.db secara async
            var companies = await _repo.GetAll();
            ComboPerusahaan.ItemsSource = companies;

            // 2. Pilih item pertama jika ada data
            if (companies.Count > 0)
                ComboPerusahaan.SelectedIndex = 0;
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            var selected = ComboPerusahaan.SelectedItem as Company;
            var inputPass = TxtPass.Password ?? ""; // Ambil input dari PasswordBox

            if (selected == null)
            {
                MessageBox.Show("Pilih perusahaan dulu!");
                return;
            }

            // VALIDASI PASSWORD: Cek input vs password yang ada di master.db
            if (!string.IsNullOrEmpty(selected.Password) && inputPass != selected.Password)
            {
                MessageBox.Show("Password salah!", "AeroGL",
                    MessageBoxButton.OK, MessageBoxImage.Error);
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

        private void ManageCompany_Click(object sender, RoutedEventArgs e)
        {
            var manager = new CompanyManagerWindow();
            manager.Owner = this; // Biar muncul di tengah GateWindow
            manager.ShowDialog();
            Window_Loaded(null, null);
        }
    }
}