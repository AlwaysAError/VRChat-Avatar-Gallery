using System;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace VrcAvatarGallery
{
    public partial class LoginForm : Form
    {
        private readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(30) };

        // Public – MainForm reads these
        public string AuthToken { get; private set; } = "";
        public LoginData? SavedLoginData { get; private set; }

        // UI
        private TextBox txtUser, txtPass, txt2FA;
        private CheckBox chkSave;
        private Label lblStatus;
        private Button btnLogin, btnCancel;

        private readonly string? existingToken;

        public LoginForm(string? existingToken = null)
        {
            this.existingToken = existingToken;
            InitializeComponent();
            BuildUI();
        }

        private void BuildUI()
        {
            this.Text = "VRChat Login";
            this.Width = 420;
            this.Height = 320;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;

            var panel = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20) };
            panel.RowCount = 6;
            panel.ColumnCount = 2;
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            // Username
            panel.Controls.Add(new Label { Text = "Username / Email:", Anchor = AnchorStyles.Left }, 0, 0);
            txtUser = new TextBox { Dock = DockStyle.Fill };
            panel.Controls.Add(txtUser, 1, 0);

            // Password
            panel.Controls.Add(new Label { Text = "Password:", Anchor = AnchorStyles.Left }, 0, 1);
            txtPass = new TextBox { Dock = DockStyle.Fill, UseSystemPasswordChar = true };
            panel.Controls.Add(txtPass, 1, 1);

            // 2FA
            panel.Controls.Add(new Label { Text = "2FA Code (optional):", Anchor = AnchorStyles.Left }, 0, 2);
            txt2FA = new TextBox { Dock = DockStyle.Fill, MaxLength = 6 };
            panel.Controls.Add(txt2FA, 1, 2);

            // Save
            chkSave = new CheckBox { Text = "Save login (encrypted)", Checked = true };
            panel.Controls.Add(chkSave, 0, 3);
            panel.SetColumnSpan(chkSave, 2);

            // Status
            lblStatus = new Label
            {
                Text = "Enter credentials and click Login.",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Color.Gray
            };
            panel.Controls.Add(lblStatus, 0, 4);
            panel.SetColumnSpan(lblStatus, 2);

            // Buttons
            var btnPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
            btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel };
            btnLogin = new Button { Text = "Login", DialogResult = DialogResult.OK };
            btnLogin.Click += async (_, __) => await DoLoginAsync();
            btnPanel.Controls.AddRange(new Control[] { btnCancel, btnLogin });
            panel.Controls.Add(btnPanel, 0, 5);
            panel.SetColumnSpan(btnPanel, 2);

            this.Controls.Add(panel);
            this.AcceptButton = btnLogin;
            this.CancelButton = btnCancel;

            // Try saved token first
            if (!string.IsNullOrEmpty(existingToken))
                _ = ValidateSavedToken(existingToken);
        }

        private async Task ValidateSavedToken(string token)
        {
            try
            {
                http.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
                var resp = await http.GetAsync("https://api.vrchat.cloud/api/1/auth/user");
                if (resp.IsSuccessStatusCode)
                {
                    AuthToken = token;
                    this.DialogResult = DialogResult.OK;
                    this.Close();
                    return;
                }
            }
            catch { }

            ShowStatus("Saved token expired – please re-login.", Color.Orange);
        }

        // -----------------------------------------------------------------
        // MAIN LOGIN LOGIC – NEVER CRASHES
        // -----------------------------------------------------------------
        private async Task DoLoginAsync()
        {
            string user = txtUser.Text.Trim();
            string pass = txtPass.Text;
            string twoFA = txt2FA.Text.Trim();

            if (string.IsNullOrEmpty(user) || string.IsNullOrEmpty(pass))
            {
                ShowError("Username and password are required.");
                return;
            }

            ShowStatus("Logging in…", Color.Blue);

            try
            {
                // 1. Normal login
                var content = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("username", user),
                    new KeyValuePair<string, string>("password", pass)
                });

                var resp = await http.PostAsync("https://api.vrchat.cloud/api/1/auth/user/login", content);
                string json = await resp.Content.ReadAsStringAsync();

                if (!resp.IsSuccessStatusCode)
                {
                    string msg = TryGetErrorMessage(json) ?? $"HTTP {resp.StatusCode}";
                    ShowError($"Login failed: {msg}");
                    return;
                }

                var loginResp = JsonSerializer.Deserialize<LoginResponse>(json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                // 2. 2FA required?
                if (loginResp!.RequiresTwoFactor)
                {
                    if (string.IsNullOrEmpty(twoFA))
                    {
                        ShowStatus("2FA code required – enter it and click Login again.", Color.Orange);
                        return;
                    }

                    var twoFAContent = new FormUrlEncodedContent(new[]
                    {
                        new KeyValuePair<string, string>("code", twoFA)
                    });

                    var twoFAResp = await http.PostAsync(
                        "https://api.vrchat.cloud/api/1/auth/twofactorauth/totp/verify", twoFAContent);

                    if (!twoFAResp.IsSuccessStatusCode)
                    {
                        ShowError("Invalid 2FA code.");
                        return;
                    }

                    // Re-login to get final token
                    resp = await http.PostAsync("https://api.vrchat.cloud/api/1/auth/user/login", content);
                    json = await resp.Content.ReadAsStringAsync();

                    if (!resp.IsSuccessStatusCode)
                    {
                        ShowError($"Login after 2FA failed: {resp.StatusCode}");
                        return;
                    }

                    loginResp = JsonSerializer.Deserialize<LoginResponse>(json,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                }

                // SUCCESS
                AuthToken = loginResp!.Token;
                ShowStatus("Login successful!", Color.Green);

                // Save encrypted
                if (chkSave.Checked)
                {
                    byte[] pwdBytes = Encoding.UTF8.GetBytes(pass);
                    byte[] encPwd = ProtectedData.Protect(pwdBytes, null, DataProtectionScope.CurrentUser);
                    SavedLoginData = new LoginData
                    {
                        Username = user,
                        EncryptedPassword = Convert.ToBase64String(encPwd),
                        TwoFactorSecret = twoFA,
                        Token = AuthToken
                    };
                    Program.SaveLoginData(SavedLoginData);
                }

                this.DialogResult = DialogResult.OK;
                this.Close();
            }
            catch (Exception ex)
            {
                // THIS IS THE KEY: NEVER LET IT CRASH
                ShowError($"Unexpected error: {ex.Message}");
            }
        }

        // -----------------------------------------------------------------
        // Helpers
        // -----------------------------------------------------------------
        private void ShowStatus(string text, Color color)
        {
            lblStatus.Text = text;
            lblStatus.ForeColor = color;
        }

        private void ShowError(string text) => ShowStatus(text, Color.Red);

        private static string? TryGetErrorMessage(string json)
        {
            try
            {
                var el = JsonSerializer.Deserialize<JsonElement>(json);
                if (el.TryGetProperty("error", out var err) &&
                    err.TryGetProperty("message", out var msg))
                    return msg.GetString();
            }
            catch { }
            return null;
        }

        private class LoginResponse
        {
            [JsonPropertyName("token")] public string Token { get; set; } = "";
            [JsonPropertyName("requiresTwoFactor")] public bool RequiresTwoFactor { get; set; }
        }
    }
}