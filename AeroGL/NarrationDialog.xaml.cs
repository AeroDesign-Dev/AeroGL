// NarrationDialog.xaml.cs
using System.Windows;

namespace AeroGL
{
    public partial class NarrationDialog : Window
    {
        public string Result { get; private set; }

        public NarrationDialog(string init)
        {
            InitializeComponent();
            Txt.Text = init ?? "";
        }

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            Result = Txt.Text;
            DialogResult = true;
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}
