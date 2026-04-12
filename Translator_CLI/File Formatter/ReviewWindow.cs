using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Web.WebView2.WinForms;
using Microsoft.Web.WebView2.Core;
using Newtonsoft.Json;

namespace TIA_Copilot_CLI
{
    public partial class ReviewWindow : Form
    {
        private WebView2 webView;
        private string _targetFilePath;

        public ReviewWindow(string filePath)
        {
            _targetFilePath = filePath;
            
            // Thiết lập cửa sổ
            this.Text = $"TIA Copilot Reviewer - {Path.GetFileName(filePath)}";
            this.Width = 1000;
            this.Height = 700;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.Icon = System.Drawing.SystemIcons.Application; // Icon mặc định

            // Khởi tạo WebView2
            webView = new WebView2
            {
                Dock = DockStyle.Fill
            };
            this.Controls.Add(webView);

            InitializeAsync();
        }

        private async void InitializeAsync()
        {
            // Nạp Runtime Chromium
            await webView.EnsureCoreWebView2Async(null);

            // Bắt sự kiện khi file HTML gọi postMessage (Lúc bấm nút Save)
            webView.CoreWebView2.WebMessageReceived += CoreWebView2_WebMessageReceived;

            // Bắt sự kiện khi HTML load xong để bắn Code từ C# vào
            webView.CoreWebView2.NavigationCompleted += CoreWebView2_NavigationCompleted;

            // Xác định đường dẫn file HTML
            string exeDir = AppDomain.CurrentDomain.BaseDirectory;
            DirectoryInfo dir = new DirectoryInfo(exeDir);
            while (dir != null && !dir.Name.Equals("Translator_CLI", StringComparison.OrdinalIgnoreCase))
                dir = dir.Parent;
            string root = dir != null ? dir.FullName : exeDir;

            string ext = Path.GetExtension(_targetFilePath).ToLower();
            string htmlFile = (ext == ".json") ? "json_review.html" : "scl_review.html";
            
            string htmlPath = Path.Combine(root, "review_assets", htmlFile);

            if (File.Exists(htmlPath))
            {
                webView.CoreWebView2.Navigate(htmlPath);
            }
            else
            {
                MessageBox.Show($"Không tìm thấy file giao diện tại: {htmlPath}", "Lỗi hệ thống", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void CoreWebView2_NavigationCompleted(object sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            if (e.IsSuccess && File.Exists(_targetFilePath))
            {
                string content = File.ReadAllText(_targetFilePath);
                
                // Đóng gói JSON gửi sang Javascript
                var payload = new
                {
                    type = "load",
                    filename = Path.GetFileName(_targetFilePath),
                    content = content
                };
                
                string jsonPayload = JsonConvert.SerializeObject(payload);
                webView.CoreWebView2.PostWebMessageAsJson(jsonPayload);
            }
        }

        private void CoreWebView2_WebMessageReceived(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            // Nhận JSON từ Javascript
            string json = e.WebMessageAsJson;
            dynamic data = JsonConvert.DeserializeObject(json);

            if (data.type == "save")
            {
                string newContent = data.content;
                // Ghi đè file vật lý
                File.WriteAllText(_targetFilePath, newContent, System.Text.Encoding.UTF8);
            }
        }

        // =====================================================================
        // HÀM HELPER: Khởi chạy Form từ Console (Chống Crash MTA Thread)
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
            if (ext == ".csv")
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"[VIEWER] Đang mở file Dữ liệu: {Path.GetFileName(filePath)} (Native App)");
                Console.ResetColor();

                try
                {
                    // Lớp khiên 1: Cố gắng ép Windows mở bằng Excel
                    Process.Start("excel.exe", $"\"{filePath}\"");
                }
                catch
                {
                    try
                    {
                        // Lớp khiên 2 (Fallback): Máy không cài Excel -> Bật bằng Notepad
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine($"[INFO] Không tìm thấy Excel, tự động chuyển sang Notepad...");
                        Console.ResetColor();
                        
                        Process.Start("notepad.exe", $"\"{filePath}\"");
                    }
                    catch (Exception)
                    {
                        // Lớp khiên 3 (Đường cùng): Nhờ Windows tự tìm app mặc định để mở
                        Process.Start(new ProcessStartInfo { FileName = filePath, UseShellExecute = true });
                    }
                }
                
                return; // XONG VIỆC, THOÁT HÀM! KHÔNG CHẠY XUỐNG WEBVIEW2 NỮA!
            }

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"[VIEWER] Đang mở Editor cho file: {Path.GetFileName(filePath)} (Chạy độc lập)");
            Console.ResetColor();

            // Khởi tạo một luồng riêng biệt cho mỗi cửa sổ được mở
            Thread staThread = new Thread(() =>
            {
                try
                {
                    // Lớp khiên bảo vệ: WinForms sẽ văng lỗi nếu gọi hàm này 2 lần trong cùng 1 App
                    if (!Application.RenderWithVisualStyles)
                    {
                        Application.EnableVisualStyles();
                        Application.SetCompatibleTextRenderingDefault(false);
                    }
                }
                catch { /* Bỏ qua nếu đã được khởi tạo ở luồng trước đó */ }

                Application.Run(new ReviewWindow(filePath));
            });

            staThread.SetApartmentState(ApartmentState.STA);
            
            // QUAN TRỌNG: Đặt thành Background Thread. 
            // Nghĩa là nếu bạn tắt cửa sổ Console đen đi, tất cả các cửa sổ Viewer sẽ tự động dọn dẹp và tắt theo, không bị kẹt lại trong Task Manager.
            staThread.IsBackground = true; 
            
            staThread.Start();
            
            // staThread.Join(); 
            // Nhờ xóa dòng này, Console của bạn sẽ không bị block nữa, trả lại con trỏ nhấp nháy để bạn gõ lệnh tiếp theo ngay lập tức!
        }
    }
}