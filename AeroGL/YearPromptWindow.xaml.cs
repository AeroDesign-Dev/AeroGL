using System;
using System.Windows;


namespace AeroGL
{
    public partial class YearPromptWindow : Window
    {
        public int SelectedYear { get; private set; }
        public YearPromptWindow()
        {
            InitializeComponent();
            TxtYear.Text = DateTime.Now.Year.ToString();
            TxtYear.Focus();
            TxtYear.SelectAll();
        }
        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            if (int.TryParse(TxtYear.Text, out int y) && y > 1900 && y < 2100)
            {
                SelectedYear = y;
                DialogResult = true;
            }
            else
            {
                MessageBox.Show("Tahun tidak valid!");
            }
        }
    }
}
