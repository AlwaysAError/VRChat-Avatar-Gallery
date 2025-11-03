using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows.Forms;

namespace VrcAvatarGallery
{
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
            // -----------------------------------------------------------------
            // 1. Global exception handlers – NEVER silent crash
            // -----------------------------------------------------------------
            Application.SetHighDpiMode(HighDpiMode.SystemAware);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            Application.ThreadException += (s, e) =>
            {
                ShowCrash($"UI THREAD EXCEPTION:\n\n{e.Exception.Message}\n\nStack:\n{e.Exception.StackTrace}");
            };

            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                ShowCrash($"UNHANDLED EXCEPTION:\n\n{e.ExceptionObject}");
            };

            // -----------------------------------------------------------------
            // 2. Normal startup – wrapped in try/catch
            // -----------------------------------------------------------------
            try
            {
                ApplicationConfiguration.Initialize();

                var saved = LoadLoginData();
                string? token = saved?.Token;

                using var loginForm = new LoginForm(token);

                DialogResult dlg;
                try
                {
                    dlg = loginForm.ShowDialog();
                }
                catch (Exception ex)
                {
                    ShowCrash($"LOGIN DIALOG CRASH:\n\n{ex.Message}\n\n{ex.StackTrace}");
                    return;
                }

                if (dlg != DialogResult.OK || string.IsNullOrEmpty(loginForm.AuthToken))
                    return;

                var mainForm = new MainForm(loginForm.AuthToken, loginForm.SavedLoginData);
                Application.Run(mainForm);
            }
            catch (Exception ex)
            {
                ShowCrash($"FATAL STARTUP ERROR:\n\n{ex.Message}\n\n{ex.StackTrace}");
            }
        }

        // -----------------------------------------------------------------
        // Helper: show a message box and exit
        // -----------------------------------------------------------------
        private static void ShowCrash(string text)
        {
            MessageBox.Show(
                text,
                "APPLICATION CRASH",
                MessageBoxButtons.OK,
                MessageBoxIcon.Stop);
            Environment.Exit(1);
        }

        // -----------------------------------------------------------------
        // Load / Save encrypted login
        // -----------------------------------------------------------------
        private static LoginData? LoadLoginData()
        {
            try
            {
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string path = Path.Combine(appData, "VrcAvatarGallery", "login.dat");
                if (!File.Exists(path)) return null;

                byte[] enc = File.ReadAllBytes(path);
                byte[] plain = ProtectedData.Unprotect(enc, null, DataProtectionScope.CurrentUser);
                return JsonSerializer.Deserialize<LoginData>(Encoding.UTF8.GetString(plain));
            }
            catch { return null; }
        }

        public static void SaveLoginData(LoginData data)
        {
            try
            {
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string dir = Path.Combine(appData, "VrcAvatarGallery");
                Directory.CreateDirectory(dir);
                string path = Path.Combine(dir, "login.dat");

                string json = JsonSerializer.Serialize(data);
                byte[] plain = Encoding.UTF8.GetBytes(json);
                byte[] enc = ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser);
                File.WriteAllBytes(path, enc);
            }
            catch { }
        }
    }
}