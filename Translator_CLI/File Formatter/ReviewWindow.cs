using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Web.WebView2.WinForms;
using Microsoft.Web.WebView2.Core;
using Newtonsoft.Json;

namespace TIA_Copilot_CLI
{
    // Review mode — determines how InitializeAsync behaves
    public enum ReviewMode { Scl, Json, Csv, Cwc }

    public partial class ReviewWindow : Form
    {
        private WebView2 webView;
        private string _targetFilePath;
        private ReviewMode _mode;

        // CWC-only fields
        private string _tempDir = null;   // temp extraction folder, cleaned up on close
        private string _zipPath = null;   // original zip path kept for toolbar actions

        // =====================================================================
        // CONSTRUCTOR — standard file types (SCL, JSON, CSV)
        // =====================================================================
        public ReviewWindow(string filePath, ReviewMode mode = ReviewMode.Scl)
        {
            _targetFilePath = filePath;
            _mode = mode;

            this.Text = $"TIA Copilot Reviewer - {Path.GetFileName(filePath)}";
            this.Width = 1000;
            this.Height = 700;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.Icon = System.Drawing.SystemIcons.Application;

            webView = new WebView2 { Dock = DockStyle.Fill };
            this.Controls.Add(webView);

            InitializeAsync();
        }

        // =====================================================================
        // CONSTRUCTOR — CWC zip preview mode
        // =====================================================================
        public ReviewWindow(string zipPath, string tempDir)
        {
            _zipPath = zipPath;
            _tempDir = tempDir;
            _mode = ReviewMode.Cwc;

            // Read control name from manifest for the title bar
            string controlName = TryReadControlName(zipPath);
            this.Text = $"TIA Copilot — CWC Preview: {controlName}";
            this.Width = 1100;
            this.Height = 750;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.Icon = System.Drawing.SystemIcons.Application;

            webView = new WebView2 { Dock = DockStyle.Fill };
            this.Controls.Add(webView);

            InitializeAsync();
        }

        // =====================================================================
        // INITIALIZE — branches on mode
        // =====================================================================
        private async void InitializeAsync()
        {
            await webView.EnsureCoreWebView2Async(null);

            if (_mode == ReviewMode.Cwc)
            {
                await InitializeCwcAsync();
                return;
            }

            // Standard flow — same as before
            webView.CoreWebView2.WebMessageReceived  += CoreWebView2_WebMessageReceived;
            webView.CoreWebView2.NavigationCompleted += CoreWebView2_NavigationCompleted;

            string exeDir  = AppDomain.CurrentDomain.BaseDirectory;
            DirectoryInfo dir = new DirectoryInfo(exeDir);
            while (dir != null && !dir.Name.Equals("Translator_CLI", StringComparison.OrdinalIgnoreCase))
                dir = dir.Parent;
            string root = dir != null ? dir.FullName : exeDir;

            string ext      = Path.GetExtension(_targetFilePath).ToLower();
            string htmlFile = (ext == ".json") ? "json_review.html" : "scl_review.html";
            string htmlPath = Path.Combine(root, "review_assets", htmlFile);

            if (File.Exists(htmlPath))
                webView.CoreWebView2.Navigate(htmlPath);
            else
                MessageBox.Show($"Không tìm thấy file giao diện tại: {htmlPath}", "Lỗi hệ thống",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        // =====================================================================
        // CWC INITIALIZE — inject mock WebCC, navigate to extracted index.html
        // =====================================================================
        private async System.Threading.Tasks.Task InitializeCwcAsync()
        {
            // 1. Dùng sessionStorage để lưu trạng thái Mode. Mặc định mới mở lên là RUNTIME!
            await webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(@"
                var currentMode = sessionStorage.getItem('_tia_cwc_mode') || 'runtime';
                var isDesign = (currentMode === 'design');

                window.WebCC = {
                    start: function(callback, contract, extensions, timeout) {
                        if (contract && contract.properties) {
                            window.WebCC._props = contract.properties;
                        }
                        setTimeout(function() { callback(true); }, 50);
                    },
                    Properties: new Proxy({}, {
                        get: function(target, key) {
                            if (window.WebCC._props && window.WebCC._props[key] !== undefined)
                                return window.WebCC._props[key];
                            return 0;
                        },
                        set: function(target, key, value) { return true; }
                    }),
                    _props: {},
                    Events:             { fire: function() {} },
                    onPropertyChanged:  { subscribe: function() {} },
                    onLanguageChanged:  { subscribe: function() {} },
                    
                    // BÍ QUYẾT LÀ Ở ĐÂY:
                    isDesignMode:       isDesign,
                    
                    language:           'en-US',
                    Extensions:         { HMI: { Style: { Name: 'FlatStyle_Dark',
                                         onchanged: { subscribe: function() {} } },
                                         Properties: { onPropertyChanged: { subscribe: function() {} } } } }
                };
            ");

            webView.CoreWebView2.WebMessageReceived += CwcToolbar_MessageReceived;
            webView.CoreWebView2.NavigationCompleted += CwcPage_NavigationCompleted;

            string indexPath = Path.Combine(_tempDir, "control", "index.html");
            if (File.Exists(indexPath))
                webView.CoreWebView2.Navigate($"file:///{indexPath.Replace('\\', '/')}");
        }

        // =====================================================================
        // CWC — inject floating toolbar after page loads
        // =====================================================================
        private async void CwcPage_NavigationCompleted(object sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            if (!e.IsSuccess) return;

            // Inject a floating toolbar overlay into the rendered CWC page
            await webView.CoreWebView2.ExecuteScriptAsync(@"
                (function() {
                    if (document.getElementById('_cwc_toolbar')) return;

                    var bar = document.createElement('div');
                    bar.id = '_cwc_toolbar';
                    bar.style.cssText = [
                        'position:fixed', 'top:0', 'left:0', 'right:0', 'z-index:999999',
                        'background:rgba(20,20,35,0.92)', 'backdrop-filter:blur(4px)',
                        'display:flex', 'align-items:center', 'gap:8px',
                        'padding:6px 12px', 'font-family:Segoe UI,sans-serif',
                        'font-size:13px', 'color:#ccc', 'border-bottom:1px solid #444'
                    ].join(';');

                    function btn(label, color, msg) {
                        var b = document.createElement('button');
                        b.textContent = label;
                        b.style.cssText = 'padding:4px 12px;border:none;border-radius:4px;cursor:pointer;' +
                                          'background:' + color + ';color:#fff;font-size:12px;font-weight:600;';
                        
                        // ĐÃ SỬA LỖI Ở DÒNG NÀY: Bỏ JSON.stringify đi
                        b.onclick = function() { window.chrome.webview.postMessage({action: msg}); };
                        
                        return b;
                    }

                    var currentMode = sessionStorage.getItem('_tia_cwc_mode') || 'runtime';
                    var modeBtnText = currentMode === 'design' ? '📐 Đang ở Design (Chuyển sang Runtime)' : '▶️ Đang ở Runtime (Chuyển sang Design)';
                    var modeBtnColor = currentMode === 'design' ? '#e67e22' : '#27ae60';

                    bar.appendChild(document.createTextNode('🔍 CWC Preview  |  '));
                    bar.appendChild(btn(modeBtnText, modeBtnColor, 'toggle_mode'));
                    bar.appendChild(document.createTextNode('  |  '));
                    
                    bar.appendChild(btn('View code.js',      '#2a6099', 'open_codejs'));
                    bar.appendChild(btn('View manifest',     '#5a3a8a', 'open_manifest'));
                    bar.appendChild(btn('View styles.css',   '#2a7a4a', 'open_css'));
                    bar.appendChild(btn('Open zip folder',   '#666',    'open_folder'));

                    var badge = document.createElement('span');
                    badge.textContent = 'Mode: ' + currentMode.toUpperCase();
                    badge.style.cssText = 'color:#f5a623;font-size:11px;margin-left:auto;font-weight:bold;';
                    bar.appendChild(badge);

                    document.body.insertBefore(bar, document.body.firstChild);
                    document.body.style.paddingTop = bar.offsetHeight + 'px';
                })();
            ");
        }

        // =====================================================================
        // CWC — handle toolbar button messages
        // =====================================================================
        private void CwcToolbar_MessageReceived(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                // Lấy chuỗi JSON từ Javascript
                string json = e.WebMessageAsJson;
                
                // Cố gắng bắt cả trường hợp Javascript gửi String thô (Fallback an toàn)
                if (json.StartsWith("\"") && json.EndsWith("\""))
                {
                    json = e.TryGetWebMessageAsString(); // <-- Sửa thành gán trực tiếp, không truyền tham số!
                }

                dynamic data = JsonConvert.DeserializeObject(json);
                string action = (string)data.action;

                switch (action)
                {
                    case "toggle_mode":
                        webView.CoreWebView2.ExecuteScriptAsync(@"`
                            var current = sessionStorage.getItem('_tia_cwc_mode') || 'runtime';
                            sessionStorage.setItem('_tia_cwc_mode', current === 'runtime' ? 'design' : 'runtime');
                            location.reload(); 
                        ");
                        break;

                    case "open_codejs":
                        OpenFileFromZipTemp("control/code.js", ReviewMode.Scl);
                        break;

                    case "open_manifest":
                        OpenFileFromZipTemp("manifest.json", ReviewMode.Json);
                        break;

                    case "open_css":
                        // Mở file CSS bằng trình editor SCL (vì nó là file text, tô màu kiểu code là hợp lý)
                        OpenFileFromZipTemp("control/styles.css", ReviewMode.Scl); 
                        break;

                    case "open_folder":
                        if (Directory.Exists(_tempDir))
                            Process.Start(new ProcessStartInfo { FileName = _tempDir, UseShellExecute = true });
                        break;
                }
            }
            catch (Exception ex)
            {
                // In lỗi ra Console thay vì nuốt âm thầm
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[CWC PREVIEW ERROR] Lỗi khi nhận lệnh từ Toolbar: {ex.Message}");
                Console.ResetColor();
            }
        }

        // Open a specific file from the temp extracted folder in a new ReviewWindow
        private void OpenFileFromZipTemp(string relPath, ReviewMode mode)
        {
            string fullPath = Path.Combine(_tempDir, relPath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(fullPath))
                OpenReviewer(fullPath);
            else
                MessageBox.Show($"File not found in zip: {relPath}", "Not Found",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        // =====================================================================
        // CWC — cleanup temp folder when preview window closes
        // =====================================================================
        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            base.OnFormClosed(e);
            if (_mode == ReviewMode.Cwc && _tempDir != null && Directory.Exists(_tempDir))
            {
                try { Directory.Delete(_tempDir, recursive: true); }
                catch { /* temp cleanup failure — leave it, OS will clean on reboot */ }
            }
        }

        // =====================================================================
        // STANDARD NavigationCompleted — sends file content to Monaco / JSONEditor
        // =====================================================================
        private void CoreWebView2_NavigationCompleted(object sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            if (e.IsSuccess && File.Exists(_targetFilePath))
            {
                string content = File.ReadAllText(_targetFilePath);

                var payload = new
                {
                    type     = "load",
                    filename = Path.GetFileName(_targetFilePath),
                    content  = content
                };

                string jsonPayload = JsonConvert.SerializeObject(payload);
                webView.CoreWebView2.PostWebMessageAsJson(jsonPayload);
            }
        }

        // =====================================================================
        // STANDARD WebMessageReceived — save content back to file
        // =====================================================================
        private void CoreWebView2_WebMessageReceived(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            string json = e.WebMessageAsJson;
            dynamic data = JsonConvert.DeserializeObject(json);

            if (data.type == "save")
            {
                string newContent = data.content;
                File.WriteAllText(_targetFilePath, newContent, System.Text.Encoding.UTF8);
            }
        }

        // =====================================================================
        // HELPER — read control name from manifest.json inside zip (for title bar)
        // =====================================================================
        private static string TryReadControlName(string zipPath)
        {
            try
            {
                using var zip = ZipFile.OpenRead(zipPath);
                var manifestEntry = zip.GetEntry("manifest.json");
                if (manifestEntry == null) return Path.GetFileNameWithoutExtension(zipPath);

                using var reader = new StreamReader(manifestEntry.Open());
                string json = reader.ReadToEnd();
                dynamic manifest = JsonConvert.DeserializeObject(json);
                return (string)manifest?.control?.identity?.name
                    ?? Path.GetFileNameWithoutExtension(zipPath);
            }
            catch
            {
                return Path.GetFileNameWithoutExtension(zipPath);
            }
        }

        // =====================================================================
        // PUBLIC STATIC — open standard file reviewer (SCL / JSON / CSV)
        // =====================================================================
        public static void OpenReviewer(string filePath)
        {
            if (!File.Exists(filePath))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[ERROR] Không tìm thấy file: {filePath}");
                Console.ResetColor();
                return;
            }

            string ext = Path.GetExtension(filePath).ToLower();

            // CSV — open in Excel or Notepad, no WebView2 needed
            if (ext == ".csv")
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"[VIEWER] Đang mở file Dữ liệu: {Path.GetFileName(filePath)} (Native App)");
                Console.ResetColor();
                try   { Process.Start("excel.exe", $"\"{filePath}\""); }
                catch
                {
                    try { Process.Start("notepad.exe", $"\"{filePath}\""); }
                    catch { Process.Start(new ProcessStartInfo { FileName = filePath, UseShellExecute = true }); }
                }
                return;
            }

            // ZIP — open CWC preview
            if (ext == ".zip")
            {
                OpenCwcPreview(filePath);
                return;
            }

            // SCL / JSON — open in Monaco / JSONEditor
            ReviewMode mode = (ext == ".json") ? ReviewMode.Json : ReviewMode.Scl;

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"[VIEWER] Đang mở Editor cho file: {Path.GetFileName(filePath)}");
            Console.ResetColor();

            Thread staThread = new Thread(() =>
            {
                try
                {
                    if (!Application.RenderWithVisualStyles)
                    {
                        Application.EnableVisualStyles();
                        Application.SetCompatibleTextRenderingDefault(false);
                    }
                }
                catch { }

                Application.Run(new ReviewWindow(filePath, mode));
            });

            staThread.SetApartmentState(ApartmentState.STA);
            staThread.IsBackground = true;
            staThread.Start();
        }

        // =====================================================================
        // PUBLIC STATIC — open CWC zip preview
        // =====================================================================
        public static void OpenCwcPreview(string zipPath)
        {
            if (!File.Exists(zipPath))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[ERROR] Zip file not found: {zipPath}");
                Console.ResetColor();
                return;
            }

            // Extract to a unique temp folder
            string tempDir = Path.Combine(Path.GetTempPath(), $"cwc_preview_{Guid.NewGuid():N}");

            try
            {
                ZipFile.ExtractToDirectory(zipPath, tempDir);
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[ERROR] Cannot extract zip: {ex.Message}");
                Console.ResetColor();
                return;
            }

            // Verify index.html actually exists inside
            string indexPath = Path.Combine(tempDir, "control", "index.html");
            if (!File.Exists(indexPath))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[ERROR] No control/index.html found inside zip.");
                Console.ResetColor();
                try { Directory.Delete(tempDir, true); } catch { }
                return;
            }

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"[CWC PREVIEW] Opening: {Path.GetFileName(zipPath)}");
            Console.ResetColor();

            Thread staThread = new Thread(() =>
            {
                try
                {
                    if (!Application.RenderWithVisualStyles)
                    {
                        Application.EnableVisualStyles();
                        Application.SetCompatibleTextRenderingDefault(false);
                    }
                }
                catch { }

                // Pass both zipPath (for title/toolbar) and tempDir (for navigation)
                Application.Run(new ReviewWindow(zipPath, tempDir));
            });

            staThread.SetApartmentState(ApartmentState.STA);
            staThread.IsBackground = true;
            staThread.Start();
        }
    }
}