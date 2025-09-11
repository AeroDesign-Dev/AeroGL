using AeroGL.Core;
using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
// using AeroGL.Core;   // hanya perlu jika kamu pakai JournalLineRecord (untuk prefill)
// using AeroGL.Data;   // TIDAK dipakai lagi di dialog ini (DB-nya dikerjakan di caller)

namespace AeroGL
{
    public partial class JournalLineDialog : Window
    {
        // ==== KONTRAK HASIL UNTUK CALLER (JournalWindow) ====
        public sealed class LineResult
        {
            public string Code2 { get; set; }     // "xxx.xxx" (2-seg)
            public string Side { get; set; }      // "D" / "K"
            public decimal Amount { get; set; }   // > 0
            public string Narration { get; set; } // gabungan 1-3 baris
        }

        public LineResult Result { get; private set; }   // <-- inilah yang dibaca JournalWindow

        // ==== OPSIONAL: info untuk prefill (mode edit) ====
        private readonly string _noTran;
        private readonly JournalLineRecord _orig;

        // >>> ctor OPSIONAL PARAMS (bisa dipanggil tanpa argumen)
        public JournalLineDialog(string noTran = null, JournalLineRecord existing = null)
        {
            InitializeComponent();
            _noTran = noTran;
            _orig = existing;

            if (_orig != null)
            {
                Title = "Ubah Baris Jurnal";
                TxtCode2.Text = _orig.Code2;
                TxtAmount.Text = _orig.Amount.ToString("N2", CultureInfo.CurrentCulture);

                var side = _orig.Side == "K" ? "K" : "D";
                var choose = CmbSide.Items.OfType<ComboBoxItem>()
                                  .FirstOrDefault(x => string.Equals(x.Content?.ToString(), side, StringComparison.OrdinalIgnoreCase));
                if (choose != null) choose.IsSelected = true;

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

            Loaded += (s, e) => TxtCode2.Focus();
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            var code2 = (TxtCode2.Text ?? "").Trim();
            var amountText = (TxtAmount.Text ?? "").Trim();

            if (string.IsNullOrWhiteSpace(code2))
            {
                MessageBox.Show("Kode rekening (2-seg) wajib diisi.", "Validasi",
                    MessageBoxButton.OK, MessageBoxImage.Exclamation);
                return;
            }

            if (!decimal.TryParse(amountText, NumberStyles.Number, CultureInfo.CurrentCulture, out var amount) || amount <= 0m)
            {
                MessageBox.Show("Jumlah harus angka > 0.", "Validasi",
                    MessageBoxButton.OK, MessageBoxImage.Exclamation);
                return;
            }

            var sideItem = CmbSide.SelectedItem as ComboBoxItem;
            var side = (sideItem?.Content ?? "D").ToString().ToUpperInvariant();
            if (side != "D" && side != "K") side = "D";

            var narr = string.Join(Environment.NewLine,
                        new[] { TxtNar1.Text, TxtNar2.Text, TxtNar3.Text }
                        .Where(s => !string.IsNullOrWhiteSpace(s))
                        .Select(s => s.Trim()));

            // >>> KEMBALIKAN HASIL KE CALLER (bukan tulis DB di sini)
            Result = new LineResult
            {
                Code2 = code2,
                Side = side,
                Amount = amount,
                Narration = narr
            };

            DialogResult = true;   // penting: supaya ShowDialog() == true
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}
