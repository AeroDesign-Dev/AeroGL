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

            var stored = global::AeroGL.Properties.Settings.Default.UtilityPassword; // bisa null atau ""

            // ---- First-time setup (belum diset) ----
            if (string.IsNullOrEmpty(stored))
            {
                if (string.IsNullOrWhiteSpace(newPwd))
                {
                    MessageBox.Show("New password tidak boleh kosong.", "AeroGL",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                global::AeroGL.Properties.Settings.Default.UtilityPassword = newPwd.Trim();
                global::AeroGL.Properties.Settings.Default.Save();

                MessageBox.Show("Password dibuat.", "AeroGL",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                DialogResult = true;
                return;
            }

            // ---- Normal change (sudah diset) ----
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

            global::AeroGL.Properties.Settings.Default.UtilityPassword = newPwd.Trim();
            global::AeroGL.Properties.Settings.Default.Save();

            MessageBox.Show("Password berhasil diubah.", "AeroGL",
                MessageBoxButton.OK, MessageBoxImage.Information);
            DialogResult = true;
        }


        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
