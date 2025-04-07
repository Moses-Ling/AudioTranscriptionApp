using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms; // Requires reference to System.Windows.Forms assembly
using System.Reflection; // For Assembly version
using System.Diagnostics; // For Process.Start

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
            DisplayVersion(); // Display version info
        }

        private void DisplayVersion()
        {
            try
            {
                var version = Assembly.GetExecutingAssembly().GetName().Version;
                VersionTextBlock.Text = $"Version: {version?.Major}.{version?.Minor}.{version?.Build}";
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to get assembly version.", ex);
                VersionTextBlock.Text = "Version: Unknown";
            }
        }


        private void LoadSettings()
        {
            // Load General Settings
            string encryptedApiKey = Properties.Settings.Default.ApiKey ?? string.Empty;
            ApiKeyBox.Password = EncryptionHelper.DecryptString(encryptedApiKey);
            ApiKeyTextBox.Text = ApiKeyBox.Password; // Sync textbox initially

            int chunkDuration = Properties.Settings.Default.ChunkDurationSeconds;
            if (chunkDuration < ChunkDurationSlider.Minimum) chunkDuration = (int)ChunkDurationSlider.Minimum;
            if (chunkDuration > ChunkDurationSlider.Maximum) chunkDuration = (int)ChunkDurationSlider.Maximum;
            ChunkDurationSlider.Value = chunkDuration;

            SavePathTextBox.Text = Properties.Settings.Default.DefaultSavePath ?? string.Empty;

            // Load Cleanup Settings
            string encryptedCleanupKey = Properties.Settings.Default.CleanupApiKey ?? string.Empty;
            CleanupApiKeyBox.Password = EncryptionHelper.DecryptString(encryptedCleanupKey);
            CleanupApiKeyTextBox.Text = CleanupApiKeyBox.Password; // Sync textbox initially
            CleanupModelComboBox.SelectedItem = FindComboBoxItem(CleanupModelComboBox, Properties.Settings.Default.CleanupModel);
            CleanupPromptTextBox.Text = Properties.Settings.Default.CleanupPrompt ?? string.Empty;

            // Load Summarize Settings
            string encryptedSummarizeKey = Properties.Settings.Default.SummarizeApiKey ?? string.Empty;
            SummarizeApiKeyBox.Password = EncryptionHelper.DecryptString(encryptedSummarizeKey);
            SummarizeApiKeyTextBox.Text = SummarizeApiKeyBox.Password; // Sync textbox initially
            SummarizeModelComboBox.SelectedItem = FindComboBoxItem(SummarizeModelComboBox, Properties.Settings.Default.SummarizeModel);
            SummarizePromptTextBox.Text = Properties.Settings.Default.SummarizePrompt ?? string.Empty;


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


                // Save General Settings (Get key from visible control)
                string whisperKey = ShowApiKeyCheckBox.IsChecked == true ? ApiKeyTextBox.Text : ApiKeyBox.Password;
                Properties.Settings.Default.ApiKey = EncryptionHelper.EncryptString(whisperKey);
                Properties.Settings.Default.ChunkDurationSeconds = (int)ChunkDurationSlider.Value;
                Properties.Settings.Default.DefaultSavePath = SavePathTextBox.Text;

                // Save Cleanup Settings (Get key from visible control)
                string cleanupKey = ShowCleanupApiKeyCheckBox.IsChecked == true ? CleanupApiKeyTextBox.Text : CleanupApiKeyBox.Password;
                Properties.Settings.Default.CleanupApiKey = EncryptionHelper.EncryptString(cleanupKey);
                Properties.Settings.Default.CleanupModel = (CleanupModelComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "gpt-4o-mini";
                Properties.Settings.Default.CleanupPrompt = CleanupPromptTextBox.Text;

                // Save Summarize Settings (Get key from visible control)
                string summarizeKey = ShowSummarizeApiKeyCheckBox.IsChecked == true ? SummarizeApiKeyTextBox.Text : SummarizeApiKeyBox.Password;
                Properties.Settings.Default.SummarizeApiKey = EncryptionHelper.EncryptString(summarizeKey);
                Properties.Settings.Default.SummarizeModel = (SummarizeModelComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "gpt-4o"; // Changed default fallback
                Properties.Settings.Default.SummarizePrompt = SummarizePromptTextBox.Text;

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
        }

        // --- API Key Visibility Toggle Handlers ---

        private void ShowApiKeyCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            ApiKeyTextBox.Text = ApiKeyBox.Password;
            ApiKeyTextBox.Visibility = Visibility.Visible;
            ApiKeyBox.Visibility = Visibility.Collapsed;
            ShowApiKeyCheckBox.Content = "Hide";
        }

        private void ShowApiKeyCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            ApiKeyBox.Password = ApiKeyTextBox.Text;
            ApiKeyTextBox.Visibility = Visibility.Collapsed;
            ApiKeyBox.Visibility = Visibility.Visible;
            ShowApiKeyCheckBox.Content = "Show";
        }

        private void ShowCleanupApiKeyCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            CleanupApiKeyTextBox.Text = CleanupApiKeyBox.Password;
            CleanupApiKeyTextBox.Visibility = Visibility.Visible;
            CleanupApiKeyBox.Visibility = Visibility.Collapsed;
            ShowCleanupApiKeyCheckBox.Content = "Hide";
        }

        private void ShowCleanupApiKeyCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            CleanupApiKeyBox.Password = CleanupApiKeyTextBox.Text;
            CleanupApiKeyTextBox.Visibility = Visibility.Collapsed;
            CleanupApiKeyBox.Visibility = Visibility.Visible;
            ShowCleanupApiKeyCheckBox.Content = "Show";
        }

         private void ShowSummarizeApiKeyCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            SummarizeApiKeyTextBox.Text = SummarizeApiKeyBox.Password;
            SummarizeApiKeyTextBox.Visibility = Visibility.Visible;
            SummarizeApiKeyBox.Visibility = Visibility.Collapsed;
            ShowSummarizeApiKeyCheckBox.Content = "Hide";
        }

        private void ShowSummarizeApiKeyCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            SummarizeApiKeyBox.Password = SummarizeApiKeyTextBox.Text;
            SummarizeApiKeyTextBox.Visibility = Visibility.Collapsed;
            SummarizeApiKeyBox.Visibility = Visibility.Visible;
            ShowSummarizeApiKeyCheckBox.Content = "Show";
        }

        // --- End API Key Visibility Toggle Handlers ---

        // --- About Tab Logic ---
        private void ViewLicenseButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // LICENSE file is in the project root, executable is likely in bin/Debug or bin/Release
                string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
                // Go up two levels (e.g., from bin/Debug to project root)
                string projectRoot = Path.GetFullPath(Path.Combine(baseDirectory, @"..\..\"));
                string licenseFilePath = Path.Combine(projectRoot, "LICENSE");

                if (File.Exists(licenseFilePath))
                {
                    Logger.Info($"Opening LICENSE file: {licenseFilePath}");
                    Process.Start(licenseFilePath);
                }
                else
                {
                    Logger.Warning($"LICENSE file not found at: {licenseFilePath}");
                    System.Windows.MessageBox.Show("LICENSE file not found.", "File Not Found", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to open LICENSE file.", ex);
                System.Windows.MessageBox.Show($"Could not open LICENSE file: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        // --- End About Tab Logic ---

        private void AboutTab_GotFocus(object sender, RoutedEventArgs e)
        {
            // Ensure the scroll viewer is at the top when the tab gets focus
            AboutScrollViewer?.ScrollToTop();
        }

        // Helper to find ComboBoxItem by content
        private System.Windows.Controls.ComboBoxItem FindComboBoxItem(System.Windows.Controls.ComboBox comboBox, string content)
        {
            foreach (System.Windows.Controls.ComboBoxItem item in comboBox.Items)
            {
                 if (item.Content?.ToString() == content)
                {
                    return item;
                }
            }
            // Return default if not found (or handle error)
             Logger.Warning($"Could not find ComboBoxItem with content '{content}'. Returning first item or null.");
             return comboBox.Items.Count > 0 ? (System.Windows.Controls.ComboBoxItem)comboBox.Items[0] : null;
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
