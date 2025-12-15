using System.Windows;

namespace AeroGL
{
    public partial class ProjectGateWindow : Window
    {
        public string ProjectCode { get; private set; }

        public ProjectGateWindow()
        {
            InitializeComponent();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // prefill untuk memudahkan (tetap bisa diubah)
            TxtKode.Text = SingleProject.Code;
            if (string.IsNullOrWhiteSpace(TxtKode.Text)) TxtKode.Focus();
            else TxtPass.Focus();
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            var kodeInput = (TxtKode.Text ?? "").Trim();
            var passInput = TxtPass.Password ?? "";

            var kodeSet = SingleProject.Code;     // sudah 3 digit (mis. "001")
            var passSet = SingleProject.Pass ?? "";

            if (string.IsNullOrWhiteSpace(kodeSet))
            {
                MessageBox.Show("Kode Proyek belum diisi (Utility → A).", "AeroGL",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // ← KUNCI: bandingkan setelah normalisasi
            if (!ProjectCodeUtil.Matches(kodeInput, kodeSet))
            {
                MessageBox.Show("Kode Proyek tidak cocok.", "AeroGL",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (!string.IsNullOrEmpty(passSet) && passInput != passSet)
            {
                MessageBox.Show("Password Proyek salah.", "AeroGL",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            ProjectCode = kodeSet; // ex: "001"
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
