using System.Windows;

namespace AeroGL
{
    public partial class PasswordPromptWindow : Window
    {
        public string EnteredPassword { get; private set; }

        public PasswordPromptWindow()
        {
            InitializeComponent();
            TxtPassword.Focus();
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            EnteredPassword = TxtPassword.Password;
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}