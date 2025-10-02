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

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            var kode = (TxtKode.Text ?? "").Trim();
            var nama = (TxtNama.Text ?? "").Trim();
            var pass = TxtPass.Password ?? "";

            if (kode.Length == 0)
            {
                MessageBox.Show("Kode proyek wajib diisi.", "AeroGL",
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

            // Simpan ke Settings
            if (!TxtKode.IsReadOnly) SingleProject.Code = kode; // kalau locked, jangan ubah
            SingleProject.Name = nama;
            SingleProject.Pass = pass;
            SingleProject.Save();

            TxtStatus.Text = $"Tersimpan: {SingleProject.Code} — {SingleProject.Name}";
            MessageBox.Show("Data proyek disimpan.", "AeroGL",
                MessageBoxButton.OK, MessageBoxImage.Information);

            DialogResult = true; // kalau mau langsung close setelah simpan, aktifkan ini
            // Close();
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
    }
}
