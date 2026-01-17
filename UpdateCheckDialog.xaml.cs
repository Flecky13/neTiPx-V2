using System;
using System.Windows;

namespace neTiPx
{
    public partial class UpdateCheckDialog : Window
    {
        public bool UserWantsUpdate { get; private set; } = false;

        public UpdateCheckDialog(string currentVersion, string newVersion)
        {
            InitializeComponent();

            CurrentVersionText.Text = $"Deine Version: {currentVersion}";
            NewVersionText.Text = $"Neue Version: {newVersion}";
        }

        private void BtnUpdaten_Click(object sender, RoutedEventArgs e)
        {
            UserWantsUpdate = true;
            DialogResult = true;
            Close();
        }

        private void BtnSpäter_Click(object sender, RoutedEventArgs e)
        {
            UserWantsUpdate = false;
            DialogResult = false;
            Close();
        }
    }
}
