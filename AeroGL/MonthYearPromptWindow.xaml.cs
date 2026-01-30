using System;
using System.Windows;
using System.Windows.Controls;


namespace AeroGL
{
    public partial class MonthYearPromptWindow : Window
    {
        public int SelectedMonth { get; private set; }
        public int SelectedYear { get; private set; }

        public MonthYearPromptWindow()
        {
            InitializeComponent();
            TxtYear.Text = DateTime.Now.Year.ToString();
            ComboMonth.SelectedIndex = DateTime.Now.Month - 2; // Default ke bulan lalu
            if (ComboMonth.SelectedIndex < 0) ComboMonth.SelectedIndex = 0;
            TxtYear.Focus();
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            if (ComboMonth.SelectedItem is ComboBoxItem item && int.TryParse(TxtYear.Text, out int y))
            {
                SelectedMonth = int.Parse(item.Tag.ToString());
                SelectedYear = y;
                DialogResult = true;
            }
        }
    }
}
