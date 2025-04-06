using System;
using System.IO;
using System.Reflection;

namespace AudioTranscriptionApp
{
    public static class Logger
    {
        private static readonly string LogFilePath;
        private static readonly object LockObject = new object(); // For thread safety

        static Logger()
        {
            try
            {
                // Log file next to the executable
                string exePath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                LogFilePath = Path.Combine(exePath ?? Environment.CurrentDirectory, "app.log");

                // Optional: Add header for new session
                Log("INFO", $"--- Log Session Started: {DateTime.Now} ---");
            }
            catch (Exception ex)
            {
                // Fallback or handle error where logging cannot be initialized
                Console.WriteLine($"FATAL: Could not initialize logger: {ex.Message}");
                LogFilePath = null; // Disable logging if path fails
            }
        }

        public static void Info(string message)
        {
            Log("INFO", message);
        }

        public static void Warning(string message)
        {
            Log("WARN", message);
        }

        public static void Error(string message, Exception ex = null)
        {
            string errorMessage = message;
            if (ex != null)
            {
                errorMessage += $"\nException: {ex.GetType().Name} - {ex.Message}\nStackTrace: {ex.StackTrace}";
            }
            Log("ERROR", errorMessage);
        }

        private static void Log(string level, string message)
        {
            if (LogFilePath == null) return; // Logging disabled

            try
            {
                // Use lock for basic thread safety when writing to the file
                lock (LockObject)
                {
                    // Append log entry to the file
                    using (StreamWriter writer = File.AppendText(LogFilePath))
                    {
                        writer.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}");
                    }
                }
            }
            catch (Exception logEx)
            {
                // Handle potential errors during logging itself (e.g., disk full, permissions)
                Console.WriteLine($"Logging Error ({level}): {message}. Failed to write to log file: {logEx.Message}");
            }
        }
    }
}
