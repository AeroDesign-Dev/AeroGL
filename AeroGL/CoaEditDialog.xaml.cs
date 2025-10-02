using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
using AeroGL.Core;

namespace AeroGL
{
    public partial class CoaEditDialog : Window
    {
        private readonly Coa _orig;
        public Coa Result { get; private set; }

        public CoaEditDialog(Coa existing)
        {
            InitializeComponent();
            _orig = existing;

            // isi ComboBox enum
            CmbType.ItemsSource = Enum.GetValues(typeof(AccountType)).Cast<AccountType>().ToList();
            CmbGrp.ItemsSource = Enum.GetValues(typeof(AccountGroup)).Cast<AccountGroup>().ToList();

            if (_orig != null)
            {
                Title = "COA — Ubah";
                TxtName.Text = _orig.Name;

                // Pecah Code3 => 3 box
                var parts = (_orig.Code3 ?? "").Split('.');
                TxtCode1.Text = parts.Length > 0 ? parts[0] : "";
                TxtCode2.Text = parts.Length > 1 ? parts[1] : "";
                TxtCode3.Text = parts.Length > 2 ? parts[2] : "";

                CmbType.SelectedItem = _orig.Type;
                CmbGrp.SelectedItem = _orig.Grp;

                // primary key tidak boleh diubah
                TxtCode1.IsEnabled = TxtCode2.IsEnabled = TxtCode3.IsEnabled = false;
            }
            else
            {
                Title = "COA — Tambah";
                // default: Debit-Aktiva biar cepat
                CmbType.SelectedItem = AccountType.Debit;
                CmbGrp.SelectedItem = AccountGroup.Aktiva;
            }
        }

        // ===================== Save / Cancel =====================
        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            var p1 = (TxtCode1.Text ?? "").Trim();
            var p2 = (TxtCode2.Text ?? "").Trim();
            var p3 = (TxtCode3.Text ?? "").Trim();
            var code3 = $"{p1}.{p2}.{p3}";

            var name = (TxtName.Text ?? "").Trim();
            var type = (AccountType?)CmbType.SelectedItem;
            var grp = (AccountGroup?)CmbGrp.SelectedItem;

            if (!Regex.IsMatch(p1, @"^\d{3}$") || !Regex.IsMatch(p2, @"^\d{3}$") || !Regex.IsMatch(p3, @"^\d{3}$"))
            {
                MessageBox.Show("Code3 harus 3-3-3 digit (xxx.xxx.xxx).", "Validasi Error",
                    MessageBoxButton.OK, MessageBoxImage.Exclamation);
                return;
            }
            if (!Regex.IsMatch(code3, @"^\d{3}\.\d{3}\.001$"))
            {
                MessageBox.Show("Format Code3 wajib xxx.xxx.001 (hanya .001 yang diizinkan).",
                    "Validasi", MessageBoxButton.OK, MessageBoxImage.Exclamation);
                return;
            }
            if (string.IsNullOrWhiteSpace(name))
            {
                MessageBox.Show("Name wajib diisi.", "Validasi Error",
                    MessageBoxButton.OK, MessageBoxImage.Exclamation);
                return;
            }
            if (type == null || grp == null)
            {
                MessageBox.Show("Type dan Group wajib dipilih.", "Validasi",
                    MessageBoxButton.OK, MessageBoxImage.Exclamation);
                return;
            }

            Result = new Coa
            {
                Code3 = code3,
                Name = name,
                Type = type.Value,
                Grp = grp.Value
            };
            DialogResult = true;
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

        // ===================== UX: 3 kotak kode =====================
        private void DigitOnly_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            // hanya digit
            e.Handled = !Regex.IsMatch(e.Text, @"^\d+$");
        }

        private void CodePart_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            var tb = sender as System.Windows.Controls.TextBox;
            if (tb == null) return;

            // auto-advance jika sudah 3 digit
            if (tb.Text.Length >= 3)
            {
                if (tb == TxtCode1) TxtCode2.Focus();
                else if (tb == TxtCode2) TxtCode3.Focus();
            }
        }

        private void CodePart_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            var tb = sender as System.Windows.Controls.TextBox;
            if (tb == null) return;

            // backspace di kosong → pindah ke sebelumnya
            if (e.Key == Key.Back && tb.SelectionStart == 0 && tb.SelectionLength == 0)
            {
                if (tb == TxtCode3) { TxtCode2.Focus(); TxtCode2.CaretIndex = TxtCode2.Text.Length; e.Handled = true; }
                else if (tb == TxtCode2) { TxtCode1.Focus(); TxtCode1.CaretIndex = TxtCode1.Text.Length; e.Handled = true; }
            }
        }
    }
}
