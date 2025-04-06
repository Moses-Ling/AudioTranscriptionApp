using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Reflection;
using System.Windows; // Required for MessageBox if DecryptString shows it on error

namespace AudioTranscriptionApp.Tests
{
    [TestClass]
    public class SettingsTests
    {
        private static MethodInfo _encryptMethod;
        private static MethodInfo _decryptMethod;

        [ClassInitialize]
        public static void ClassInitialize(TestContext context)
        {
            // Use reflection to get access to the private static methods in MainWindow
            // Note: This relies on the MainWindow class being in the AudioTranscriptionApp namespace
            var mainWindowType = typeof(MainWindow); // Assumes MainWindow is accessible
            _encryptMethod = mainWindowType.GetMethod("EncryptString", BindingFlags.NonPublic | BindingFlags.Static);
            _decryptMethod = mainWindowType.GetMethod("DecryptString", BindingFlags.NonPublic | BindingFlags.Static);

            if (_encryptMethod == null)
            {
                throw new InvalidOperationException("Could not find private static method 'EncryptString' in MainWindow.");
            }
            if (_decryptMethod == null)
            {
                throw new InvalidOperationException("Could not find private static method 'DecryptString' in MainWindow.");
            }
        }

        [TestMethod]
        public void EncryptDecrypt_RoundTrip_ShouldReturnOriginal()
        {
            // Arrange
            string originalApiKey = "test-api-key-12345";

            // Act
            string encrypted = (string)_encryptMethod.Invoke(null, new object[] { originalApiKey });
            Assert.IsNotNull(encrypted, "Encryption returned null.");
            Assert.AreNotEqual(originalApiKey, encrypted, "Encrypted string should not match original.");

            // Suppress MessageBox popups during decryption failure tests if needed
            // This is tricky as MessageBox is static. A better approach is refactoring
            // the DecryptString method to not show MessageBox directly or use DI.
            // For now, we assume decryption succeeds in this test.

            string decrypted = (string)_decryptMethod.Invoke(null, new object[] { encrypted });

            // Assert
            Assert.AreEqual(originalApiKey, decrypted, "Decrypted string does not match original.");
        }

        [TestMethod]
        public void Encrypt_NullOrEmpty_ShouldReturnEmpty()
        {
            // Act
            string encryptedNull = (string)_encryptMethod.Invoke(null, new object[] { null });
            string encryptedEmpty = (string)_encryptMethod.Invoke(null, new object[] { "" });

            // Assert
            Assert.AreEqual(string.Empty, encryptedNull, "Encrypting null should return empty string.");
            Assert.AreEqual(string.Empty, encryptedEmpty, "Encrypting empty string should return empty string.");
        }

         [TestMethod]
        public void Decrypt_NullOrEmpty_ShouldReturnEmpty()
        {
            // Act
            string decryptedNull = (string)_decryptMethod.Invoke(null, new object[] { null });
            string decryptedEmpty = (string)_decryptMethod.Invoke(null, new object[] { "" });

            // Assert
            Assert.AreEqual(string.Empty, decryptedNull, "Decrypting null should return empty string.");
            Assert.AreEqual(string.Empty, decryptedEmpty, "Decrypting empty string should return empty string.");
        }

        // Note: Testing the failure cases of DecryptString (CryptographicException, FormatException)
        // is difficult here because they show a MessageBox and modify Properties.Settings.
        // This indicates the helper methods should ideally be refactored into a separate,
        // testable utility class that doesn't have UI or direct settings side effects.
    }
}
