using AudioTranscriptionApp.ViewModels;
using System.ComponentModel;
using System.Windows;

namespace AudioTranscriptionApp
{
    public partial class MainWindow : Window
    {
        private MainViewModel _viewModel;

        public MainWindow()
        {
            InitializeComponent();
            _viewModel = (MainViewModel)FindResource("MainViewModel");
        }

        private void ApiKeyBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (_viewModel != null)
            {
                _viewModel.ApiKey = ApiKeyBox.Password;
            }
        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            _viewModel?.Cleanup();
        }
    }
}
