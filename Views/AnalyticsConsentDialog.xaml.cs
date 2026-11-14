using System.Windows;

namespace MasseterDrop.Views
{
    public partial class AnalyticsConsentDialog : Window
    {
        public bool ConsentGranted { get; private set; } = false;

        public AnalyticsConsentDialog()
        {
            InitializeComponent();
        }

        private void AllowButton_Click(object sender, RoutedEventArgs e)
        {
            ConsentGranted = true;
            DialogResult = true;
            Close();
        }

        private void DeclineButton_Click(object sender, RoutedEventArgs e)
        {
            ConsentGranted = false;
            DialogResult = false;
            Close();
        }
    }
}
