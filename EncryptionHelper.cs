using System;
using System.Security.Cryptography;
using System.Text;
using System.Windows; // For MessageBox on decryption error

namespace AudioTranscriptionApp
{
    public static class EncryptionHelper
    {
        // Optional extra entropy - makes it slightly harder to decrypt if someone gains access
        // to the user's profile AND knows this salt.
        private static readonly byte[] s_entropy = Encoding.Unicode.GetBytes("AudioAppSalt349857");

        public static string EncryptString(string input)
        {
            if (string.IsNullOrEmpty(input)) return string.Empty;
            try
            {
                byte[] encryptedData = ProtectedData.Protect(
                    Encoding.Unicode.GetBytes(input),
                    s_entropy,
                    DataProtectionScope.CurrentUser); // Scope to the current user
                return Convert.ToBase64String(encryptedData);
            }
            catch (CryptographicException ex)
            {
                Logger.Error("Error encrypting data.", ex);
                System.Windows.MessageBox.Show($"Error encrypting data: {ex.Message}", "Encryption Error", MessageBoxButton.OK, MessageBoxImage.Error); // Explicit namespace
                return string.Empty; // Or handle differently
            }
        }

        public static string DecryptString(string encryptedData)
        {
            if (string.IsNullOrEmpty(encryptedData)) return string.Empty;
            try
            {
                byte[] decryptedData = ProtectedData.Unprotect(
                    Convert.FromBase64String(encryptedData),
                    s_entropy,
                    DataProtectionScope.CurrentUser);
                return Encoding.Unicode.GetString(decryptedData);
            }
            catch (CryptographicException ex) // Added ex variable
            {
                Logger.Error("Error decrypting data (CryptographicException). Clearing stored key.", ex);
                // Handle cases where decryption fails (e.g., data corruption, moved to different user)
                // Clear the invalid setting to prevent repeated errors
                Properties.Settings.Default.ApiKey = string.Empty;
                Properties.Settings.Default.Save();
                System.Windows.MessageBox.Show("API Key could not be decrypted. It might be corrupted or from a different user profile. Please re-enter your API key in Settings.", "Decryption Error", MessageBoxButton.OK, MessageBoxImage.Warning); // Explicit namespace
                return string.Empty;
            }
            catch (FormatException ex) // Added ex variable
            {
                 Logger.Error("Error decrypting data (FormatException - invalid Base64). Clearing stored key.", ex);
                 // Clear the invalid setting
                 Properties.Settings.Default.ApiKey = string.Empty;
                 Properties.Settings.Default.Save();
                 System.Windows.MessageBox.Show("Stored API Key format is invalid. Please re-enter your API key in Settings.", "Format Error", MessageBoxButton.OK, MessageBoxImage.Warning); // Explicit namespace
                 return string.Empty;
            }
        }
    }
}
