using AeroGL.Data;
using System.Windows;


namespace AeroGL
{
    public partial class ChangePasswordWindow : Window
    {
        public ChangePasswordWindow()
        {
            InitializeComponent();
            TxtOld.Focus();
        }

        private void CkShow_Checked(object sender, RoutedEventArgs e)
        {
            // sinkronisasi PasswordBox <-> TextBox
            if (CkShow.IsChecked == true)
            {
                TxtOldShow.Text = TxtOld.Password;
                TxtNewShow.Text = TxtNew.Password;
                TxtOld.Visibility = Visibility.Collapsed;
                TxtNew.Visibility = Visibility.Collapsed;
                TxtOldShow.Visibility = Visibility.Visible;
                TxtNewShow.Visibility = Visibility.Visible;
                TxtOldShow.Focus();
            }
            else
            {
                TxtOld.Password = TxtOldShow.Text;
                TxtNew.Password = TxtNewShow.Text;
                TxtOldShow.Visibility = Visibility.Collapsed;
                TxtNewShow.Visibility = Visibility.Collapsed;
                TxtOld.Visibility = Visibility.Visible;
                TxtNew.Visibility = Visibility.Visible;
                TxtOld.Focus();
            }
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            var oldPwd = (CkShow.IsChecked == true) ? TxtOldShow.Text : TxtOld.Password;
            var newPwd = (CkShow.IsChecked == true) ? TxtNewShow.Text : TxtNew.Password;

            // 1. Ambil password lama dari Database PT Aktif
            // Gunakan Key "UtilityPassword" agar sinkron dengan Segmen 2
            var stored = AccountConfig.Get("UtilityPassword");

            // ---- First-time setup (belum pernah diset di DB) ----
            if (string.IsNullOrEmpty(stored))
            {
                if (string.IsNullOrWhiteSpace(newPwd))
                {
                    MessageBox.Show("New password tidak boleh kosong.", "AeroGL",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Simpan ke Database PT aktif
                AccountConfig.Set("UtilityPassword", newPwd.Trim());

                MessageBox.Show("Password Utility berhasil dibuat di Database.", "AeroGL",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                DialogResult = true;
                return;
            }

            // ---- Normal change (sudah ada password di DB) ----
            if (oldPwd != stored)
            {
                MessageBox.Show("Old password salah.", "AeroGL",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (string.IsNullOrWhiteSpace(newPwd))
            {
                MessageBox.Show("New password tidak boleh kosong.", "AeroGL",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Update nilai baru ke Database
            AccountConfig.Set("UtilityPassword", newPwd.Trim());

            MessageBox.Show("Password Utility berhasil diperbarui.", "AeroGL",
                MessageBoxButton.OK, MessageBoxImage.Information);
            DialogResult = true;
        }


        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
