using System.Windows;

namespace AeroGL
{
    public partial class EntryKodeProyekWindow : Window
    {
        public EntryKodeProyekWindow()
        {
            InitializeComponent();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // load dari Settings
            TxtKode.Text = SingleProject.Code;
            TxtNama.Text = SingleProject.Name;
            TxtPass.Password = SingleProject.Pass;

            bool hasCode = !string.IsNullOrWhiteSpace(TxtKode.Text);
            TxtKode.IsReadOnly = hasCode;   // LOCK: kalau sudah pernah diset, kodenya dikunci
            TxtKode.ToolTip = hasCode ? "Kode proyek sudah dikunci (single project)." : null;

            TxtStatus.Text = hasCode
                ? $"Tersimpan: {SingleProject.Code} — {SingleProject.Name}"
                : "Belum ada proyek tersimpan.";

            // Fokus yang nyaman
            if (!hasCode) TxtKode.Focus();
            else if (string.IsNullOrWhiteSpace(TxtNama.Text)) TxtNama.Focus();
            else TxtPass.Focus();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter) { BtnSave_Click(sender, e); }
            if (e.Key == System.Windows.Input.Key.Escape) { Close(); }
        }

        private void TxtKode_LostFocus(object sender, RoutedEventArgs e)
        {
            var norm = ProjectCodeUtil.Normalize(TxtKode.Text);
            if (!string.IsNullOrEmpty(norm)) TxtKode.Text = norm;  // tampilkan 3 digit
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            // normalisasi dulu
            var kodeNorm = ProjectCodeUtil.Normalize(TxtKode.Text);
            var nama = (TxtNama.Text ?? "").Trim();
            var pass = TxtPass.Password ?? "";

            if (string.IsNullOrEmpty(kodeNorm))
            {
                MessageBox.Show("Kode proyek harus angka (maks 3 digit).", "AeroGL",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                TxtKode.Focus();
                return;
            }
            if (nama.Length == 0)
            {
                MessageBox.Show("Nama proyek wajib diisi.", "AeroGL",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                TxtNama.Focus();
                return;
            }

            if (!TxtKode.IsReadOnly) SingleProject.Code = kodeNorm; // simpan sudah dalam format "001"
            SingleProject.Name = nama;
            SingleProject.Pass = pass;
            SingleProject.Save();

            TxtStatus.Text = $"Tersimpan: {SingleProject.Code} — {SingleProject.Name}";
            MessageBox.Show("Data proyek disimpan.", "AeroGL",
                MessageBoxButton.OK, MessageBoxImage.Information);

            DialogResult = true;
        }
    }
}
