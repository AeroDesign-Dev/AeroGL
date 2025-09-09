using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

using AeroGL.Core;
using AeroGL.Data;

namespace AeroGL
{
    public partial class JournalLineDialog : Window
    {
        private readonly string _noTran;
        private readonly JournalLineRecord _orig;
        private readonly IJournalLineRepository _repo = new JournalLineRepository();

        public JournalLineDialog(string noTran, JournalLineRecord existing)
        {
            InitializeComponent();
            _noTran = noTran;
            _orig = existing;

            if (_orig != null)
            {
                Title = "Ubah Baris Jurnal";
                TxtCode2.Text = _orig.Code2;
                TxtAmount.Text = _orig.Amount.ToString("N2");
                var side = _orig.Side == "K" ? "K" : "D";
                (CmbSide.Items.Cast<ComboBoxItem>().First(x => (string)x.Content == side)).IsSelected = true;

                // pecah narasi max 3 baris
                var parts = (_orig.Narration ?? "").Replace("\r\n", "\n").Split('\n');
                if (parts.Length > 0) TxtNar1.Text = parts[0];
                if (parts.Length > 1) TxtNar2.Text = parts[1];
                if (parts.Length > 2) TxtNar3.Text = parts[2];
            }
            else
            {
                Title = "Tambah Baris Jurnal";
            }
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            var code2 = (TxtCode2.Text ?? "").Trim();
            var amountText = (TxtAmount.Text ?? "").Trim();
            decimal amount;
            if (string.IsNullOrWhiteSpace(code2) || !decimal.TryParse(amountText, NumberStyles.Any, CultureInfo.CurrentCulture, out amount))
            {
                MessageBox.Show("Isi rekening 2-seg dan jumlah yang valid.", "Validasi",
                    MessageBoxButton.OK, MessageBoxImage.Exclamation);
                return;
            }

            var side = ((ComboBoxItem)CmbSide.SelectedItem).Content.ToString(); // 'D'/'K'
            var nar = string.Join("\r\n", new[] { TxtNar1.Text, TxtNar2.Text, TxtNar3.Text }
                                             .Where(s => !string.IsNullOrWhiteSpace(s?.Trim()))
                                             .Select(s => s.Trim()));

            if (_orig == null)
            {
                // insert
                var line = new JournalLine { NoTran = _noTran, Code2 = code2, Side = side, Amount = amount, Narration = nar };
                _repo.Insert(line).Wait();
            }
            else
            {
                // update
                var lineNew = new JournalLine { NoTran = _noTran, Code2 = code2, Side = side, Amount = amount, Narration = nar };
                _repo.UpdateById(_orig.Id, lineNew).Wait();
            }

            DialogResult = true;
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}
