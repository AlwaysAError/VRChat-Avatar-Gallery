using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace VrcAvatarGallery
{
    public partial class MainForm : Form
    {
        private const string IdsFile = "avatars.txt";
        private const string CacheFile = "avatars.json";

        private readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(30) };
        private readonly List<AvatarInfo> avatars = new();

        // REMOVED readonly FROM THESE 4 FIELDS
        private FlowLayoutPanel gallery;
        private TextBox txtAddId;
        private Button btnAdd;
        private Button btnRefresh;

        // Authentication – mutable
        private string authToken = "";
        private LoginData? loginData;

        public MainForm(string token, LoginData? data)
        {
            authToken = token;
            loginData = data;

            InitializeComponent();
            BuildUI();
            LoadIdsFromFile();
            _ = LoadCacheOrRefreshAsync();
        }

        private void BuildUI()
        {
            this.Text = "VRChat Avatar Gallery";
            this.Width = 1150;
            this.Height = 720;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.AutoScaleMode = AutoScaleMode.Font;

            // Top panel
            var top = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 60,
                Padding = new Padding(10),
                ColumnCount = 3
            };
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 70));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            txtAddId = new TextBox { Text = "", Width = 380 };
            btnAdd = new Button { Text = "Add", Width = 80 };
            btnRefresh = new Button { Text = "Refresh All", Width = 120 };

            top.Controls.Add(txtAddId, 0, 0);
            top.Controls.Add(btnAdd, 1, 0);
            top.Controls.Add(btnRefresh, 2, 0);

            btnAdd.Click += BtnAdd_Click;
            btnRefresh.Click += async (_, __) => await RefreshAllAsync();

            // Gallery
            gallery = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                Padding = new Padding(15),
                BackColor = Color.FromArgb(248, 248, 248)
            };

            this.Controls.Add(gallery);
            this.Controls.Add(top);
        }

        // -------------------------------------------------------------
        // Add button
        // -------------------------------------------------------------
        private async void BtnAdd_Click(object? sender, EventArgs e)
        {
            string raw = txtAddId.Text.Trim();

            if (!IsValidAvatarId(raw))
            {
                MessageBox.Show(
                    "Invalid avatar ID.\n\nMust start with 'avtr_' followed by a UUID.\nExample: avtr_5f2a3b4c-1d2e-3f4a-5b6c-7d8e9f0a1b2c",
                    "Invalid ID", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (avatars.Any(a => a.Id.Equals(raw, StringComparison.OrdinalIgnoreCase)))
            {
                MessageBox.Show("Avatar already in the list.", "Info",
                                MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            btnAdd.Enabled = txtAddId.Enabled = false;

            var info = await FetchAvatarAsync(raw);
            if (info != null)
            {
                avatars.Add(info);
                SaveIdsToFile();
                SaveCacheToFile();
                RenderAvatarCard(info);
            }
            else
            {
                MessageBox.Show("Could not fetch avatar – it may be private, deleted, or a network issue.",
                                "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

            txtAddId.Text = "";
            txtAddId.Enabled = btnAdd.Enabled = true;
        }

        // -------------------------------------------------------------
        // Refresh All
        // -------------------------------------------------------------
        private async Task RefreshAllAsync()
        {
            btnRefresh.Enabled = false;
            gallery.Controls.Clear();
            avatars.Clear();

            var ids = LoadIdsFromFileRaw();
            int ok = 0, fail = 0;

            foreach (var id in ids)
            {
                var info = await FetchAvatarAsync(id);
                if (info != null)
                {
                    avatars.Add(info);
                    RenderAvatarCard(info);
                    ok++;
                }
                else fail++;
            }

            SaveCacheToFile();
            MessageBox.Show($"Refresh complete – {ok} OK, {fail} failed.",
                            "Result", MessageBoxButtons.OK, MessageBoxIcon.Information);
            btnRefresh.Enabled = true;
        }

        // -------------------------------------------------------------
        // File helpers
        // -------------------------------------------------------------
        private void LoadIdsFromFile()
        {
            if (File.Exists(IdsFile))
                File.ReadAllLines(IdsFile)
                    .Select(l => l.Trim())
                    .Where(IsValidAvatarId)
                    .ToList();
        }

        private List<string> LoadIdsFromFileRaw()
        {
            if (!File.Exists(IdsFile)) return new();
            return File.ReadAllLines(IdsFile)
                       .Select(l => l.Trim())
                       .Where(IsValidAvatarId)
                       .ToList();
        }

        private void SaveIdsToFile()
        {
            File.WriteAllLines(IdsFile, avatars.Select(a => a.Id));
        }

        private async Task LoadCacheOrRefreshAsync()
        {
            if (File.Exists(CacheFile))
            {
                try
                {
                    string json = await File.ReadAllTextAsync(CacheFile);
                    var cached = JsonSerializer.Deserialize<List<AvatarInfo>>(json,
                                 new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    if (cached != null)
                    {
                        avatars.Clear();
                        avatars.AddRange(cached);
                        foreach (var a in cached) RenderAvatarCard(a);
                        return;
                    }
                }
                catch { }
            }
            await RefreshAllAsync();
        }

        private void SaveCacheToFile()
        {
            string json = JsonSerializer.Serialize(avatars,
                new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(CacheFile, json);
        }

        // -------------------------------------------------------------
        // AUTHENTICATED FETCH + TOKEN REFRESH
        // -------------------------------------------------------------
        private async Task<AvatarInfo?> FetchAvatarAsync(string id)
        {
            try
            {
                var req = new HttpRequestMessage(HttpMethod.Get,
                    $"https://api.vrchat.cloud/api/1/avatars/{id}");
                req.Headers.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", authToken);

                var resp = await http.SendAsync(req);

                if (!resp.IsSuccessStatusCode)
                {
                    if (resp.StatusCode == System.Net.HttpStatusCode.Unauthorized && loginData != null)
                    {
                        if (await RefreshTokenAsync())
                        {
                            req.Headers.Authorization =
                                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", authToken);
                            resp = await http.SendAsync(req);
                        }
                    }

                    if (!resp.IsSuccessStatusCode)
                    {
                        Console.WriteLine($"API error {id}: {resp.StatusCode}");
                        return null;
                    }
                }

                string json = await resp.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<AvatarInfo>(json,
                       new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Fetch failed {id}: {ex.Message}");
                return null;
            }
        }

        private async Task<bool> RefreshTokenAsync()
        {
            if (loginData == null) return false;

            try
            {
                byte[] enc = Convert.FromBase64String(loginData.EncryptedPassword);
                byte[] pwd = ProtectedData.Unprotect(enc, null, DataProtectionScope.CurrentUser);
                string password = Encoding.UTF8.GetString(pwd);

                var content = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("username", loginData.Username),
                    new KeyValuePair<string, string>("password", password)
                });

                var resp = await http.PostAsync("https://api.vrchat.cloud/api/1/auth/user/login", content);
                if (!resp.IsSuccessStatusCode) return false;

                var obj = JsonSerializer.Deserialize<LoginResponse>(
                    await resp.Content.ReadAsStringAsync(),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                authToken = obj.Token;
                loginData.Token = obj.Token;
                Program.SaveLoginData(loginData);
                return true;
            }
            catch
            {
                MessageBox.Show("Session expired – please restart and log in again.", "Auth Error");
                return false;
            }
        }

        private class LoginResponse
        {
            [JsonPropertyName("token")] public string Token { get; set; } = "";
            [JsonPropertyName("requiresTwoFactor")] public bool RequiresTwoFactor { get; set; }
        }

        // -------------------------------------------------------------
        // Validation
        // -------------------------------------------------------------
        private static bool IsValidAvatarId(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return false;
            if (!s.StartsWith("avtr_", StringComparison.OrdinalIgnoreCase)) return false;
            if (s.Length < 37) return false;

            string guid = s.Substring(5).Trim();
            guid = new string(guid.Where(c => !char.IsWhiteSpace(c) && c != '-').ToArray());

            return guid.Length == 32 && guid.All(char.IsAsciiHexDigit) && Guid.TryParse(guid, out _);
        }

        // -------------------------------------------------------------
        // Rendering
        // -------------------------------------------------------------
        private void RenderAvatarCard(AvatarInfo info)
        {
            var card = new Panel
            {
                Width = 240,
                Height = 340,
                Margin = new Padding(12),
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Color.White
            };

            var pb = new PictureBox
            {
                SizeMode = PictureBoxSizeMode.Zoom,
                Height = 160,
                Dock = DockStyle.Top,
                Tag = info.ImageUrl
            };
            _ = LoadImageAsync(pb);

            var lblName = new Label
            {
                Text = Truncate(info.Name, 28),
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
                Dock = DockStyle.Top,
                Height = 40,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(8, 8, 8, 0)
            };

            var lblId = new Label
            {
                Text = $"ID: {info.Id}",
                ForeColor = Color.DarkGray,
                Dock = DockStyle.Top,
                Height = 22,
                Padding = new Padding(8, 0, 8, 0)
            };

            var lblAuthor = new Label
            {
                Text = $"by {Truncate(info.AuthorName, 22)}",
                ForeColor = Color.Gray,
                Dock = DockStyle.Top,
                Height = 22,
                Padding = new Padding(8, 0, 8, 0)
            };

            var link = new LinkLabel
            {
                Text = "Open on VRChat",
                LinkColor = Color.RoyalBlue,
                Dock = DockStyle.Top,
                Height = 26,
                Padding = new Padding(8, 4, 8, 0),
                Tag = $"https://vrchat.com/home/avatar/{info.Id}"
            };
            link.LinkClicked += (_, __) =>
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = link.Tag!.ToString()!,
                        UseShellExecute = true
                    });
                }
                catch { }
            };

            var btnRemove = new Button
            {
                Text = "Remove",
                Dock = DockStyle.Bottom,
                Height = 30,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.IndianRed,
                ForeColor = Color.White
            };
            btnRemove.Click += (_, __) =>
            {
                if (MessageBox.Show($"Remove \"{info.Name}\"?", "Confirm",
                                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                {
                    avatars.Remove(info);
                    SaveIdsToFile();
                    SaveCacheToFile();
                    gallery.Controls.Remove(card);
                }
            };

            card.Controls.AddRange(new Control[] { btnRemove, link, lblAuthor, lblId, lblName, pb });
            gallery.Controls.Add(card);
        }

        private async Task LoadImageAsync(PictureBox pb)
        {
            try
            {
                string? url = pb.Tag?.ToString();
                if (string.IsNullOrEmpty(url)) return;
                byte[] data = await http.GetByteArrayAsync(url);
                using var ms = new MemoryStream(data);
                pb.Image = Image.FromStream(ms);
            }
            catch { }
        }

        private static string Truncate(string? s, int max) =>
            string.IsNullOrEmpty(s) ? "" :
            s.Length <= max ? s : s.Substring(0, max - 1) + "…";
    }
}