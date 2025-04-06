using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
// Need this for FolderBrowserDialog
using System.Windows.Forms; // Requires reference to System.Windows.Forms assembly

namespace AudioTranscriptionApp
{
    /// <summary>
    /// Interaction logic for SettingsWindow.xaml
    /// </summary>
    public partial class SettingsWindow : Window
    {
        public SettingsWindow()
        {
            InitializeComponent();
            Logger.Info("Settings window opened.");
            LoadSettings();
        }

        private void LoadSettings()
        {
            // Load API Key (decrypt)
            string encryptedApiKey = Properties.Settings.Default.ApiKey ?? string.Empty;
            ApiKeyBox.Password = EncryptionHelper.DecryptString(encryptedApiKey);

            // Load Chunk Duration
            int chunkDuration = Properties.Settings.Default.ChunkDurationSeconds;
            // Ensure value is within slider bounds
            if (chunkDuration < ChunkDurationSlider.Minimum) chunkDuration = (int)ChunkDurationSlider.Minimum;
            if (chunkDuration > ChunkDurationSlider.Maximum) chunkDuration = (int)ChunkDurationSlider.Maximum;
            ChunkDurationSlider.Value = chunkDuration;
            // TextBox is bound to Slider, no need to set it directly

            // Load Default Save Path
            SavePathTextBox.Text = Properties.Settings.Default.DefaultSavePath ?? string.Empty;
            Logger.Info("Settings loaded into UI.");
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                 // Validate Save Path (optional, but good practice)
                 string savePath = SavePathTextBox.Text;
                 if (!string.IsNullOrEmpty(savePath) && !Directory.Exists(savePath))
                 {
                    // Use System.Windows.MessageBox explicitly
                    if (System.Windows.MessageBox.Show($"The specified save path does not exist:\n{savePath}\n\nDo you want to create it?",
                                       "Create Directory?", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                    {
                        try
                        {
                            Logger.Info($"Attempting to create directory: {savePath}");
                            Directory.CreateDirectory(savePath);
                            Logger.Info("Directory created successfully.");
                        }
                        catch (Exception ex)
                        {
                            Logger.Error($"Failed to create directory: {savePath}", ex);
                            // Use System.Windows.MessageBox explicitly
                            System.Windows.MessageBox.Show($"Failed to create directory: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                            return; // Don't save if directory creation failed
                        }
                    }
                   else
                   {
                       // User chose not to create, maybe highlight the field or just don't save path
                       // For simplicity, we'll just proceed but the path might be invalid later
                   }
                }


                // Save API Key (encrypt)
                Properties.Settings.Default.ApiKey = EncryptionHelper.EncryptString(ApiKeyBox.Password);

                // Save Chunk Duration
                Properties.Settings.Default.ChunkDurationSeconds = (int)ChunkDurationSlider.Value;

                // Save Default Save Path
                Properties.Settings.Default.DefaultSavePath = SavePathTextBox.Text;

                // Persist settings
                Properties.Settings.Default.Save();
                Logger.Info("Settings saved.");

                this.DialogResult = true; // Indicate settings were saved
                this.Close();
             }
             catch (Exception ex)
             {
                 Logger.Error("Error occurred while saving settings.", ex);
                 // Use System.Windows.MessageBox explicitly
                 System.Windows.MessageBox.Show($"Error saving settings: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
             }
         }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            Logger.Info("Settings window cancelled.");
            this.DialogResult = false;
            this.Close();
        }

        private void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            Logger.Info("Browse button clicked.");
            using (var dialog = new FolderBrowserDialog())
            {
                // Set initial directory if one is already set
                if (!string.IsNullOrEmpty(SavePathTextBox.Text) && Directory.Exists(SavePathTextBox.Text))
                {
                    dialog.SelectedPath = SavePathTextBox.Text;
                }
                else
                {
                     // Optionally set a default starting path, e.g., MyDocuments
                     dialog.SelectedPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                }

                dialog.Description = "Select Default Save Folder";
                dialog.ShowNewFolderButton = true;

                // ShowDialog requires a handle, need to get it from the WPF window
                var helper = new System.Windows.Interop.WindowInteropHelper(this);
                DialogResult result = dialog.ShowDialog(new Win32Window(helper.Handle));

                if (result == System.Windows.Forms.DialogResult.OK)
                {
                    Logger.Info($"User selected save path: {dialog.SelectedPath}");
                    SavePathTextBox.Text = dialog.SelectedPath;
                }
            }
        }

        private void ChunkDurationSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            // The TextBox is bound to the slider value, so this handler might not be strictly necessary
            // unless you need to perform additional actions when the value changes.
            // ChunkDurationTextBox.Text = $"{e.NewValue:N0}"; // Example if not using binding
        }

        // Helper class to wrap the WPF window handle for FolderBrowserDialog
        private class Win32Window : System.Windows.Forms.IWin32Window
        {
            public IntPtr Handle { get; private set; }
            public Win32Window(IntPtr handle)
            {
                Handle = handle;
            }
        }
    }
}
