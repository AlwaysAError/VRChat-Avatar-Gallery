using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows.Forms;

namespace VrcAvatarGallery
{
    // --------------------------------------------------------------
    // Public data class – used by LoginForm and MainForm
    // --------------------------------------------------------------
    public class LoginData
    {
        public string Username { get; set; } = "";
        public string EncryptedPassword { get; set; } = "";
        public string? TwoFactorSecret { get; set; }
        public string? Token { get; set; }
    }

    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            ApplicationConfiguration.Initialize();

            // Try to load a saved login
            var saved = LoadLoginData();
            string? token = saved?.Token;

            // Show login dialog
            using var loginForm = new LoginForm(token);
            if (loginForm.ShowDialog() != DialogResult.OK ||
                string.IsNullOrEmpty(loginForm.AuthToken))
            {
                return; // user cancelled
            }

            // Start the main UI
            var mainForm = new MainForm(loginForm.AuthToken, loginForm.SavedLoginData);
            Application.Run(mainForm);
        }

        // ----------------------------------------------------------
        // Load encrypted login from %APPDATA%
        // ----------------------------------------------------------
        private static LoginData? LoadLoginData()
        {
            try
            {
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string path = Path.Combine(appData, "VrcAvatarGallery", "login.dat");
                if (!File.Exists(path)) return null;

                byte[] encrypted = File.ReadAllBytes(path);
                byte[] plain = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
                string json = Encoding.UTF8.GetString(plain);
                return JsonSerializer.Deserialize<LoginData>(json);
            }
            catch
            {
                return null;
            }
        }

        // ----------------------------------------------------------
        // Save encrypted login
        // ----------------------------------------------------------
        public static void SaveLoginData(LoginData data)
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string dir = Path.Combine(appData, "VrcAvatarGallery");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, "login.dat");

            string json = JsonSerializer.Serialize(data);
            byte[] plain = Encoding.UTF8.GetBytes(json);
            byte[] encrypted = ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser);
            File.WriteAllBytes(path, encrypted);
        }
    }
}