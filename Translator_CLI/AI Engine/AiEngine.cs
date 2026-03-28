using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace TIA_Copilot_CLI
{
    public static class AiEngine
    {
        public static string PYTHON_EXE_PATH = "";
        public static string PYTHON_SCRIPT_PATH = "";
        public static void InitializePaths()
        {
            // Lấy thư mục gốc nơi file .exe đang chạy
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            DirectoryInfo currentDir = new DirectoryInfo(baseDir);

            string backendFolder = null;

            // Quét ngược dần lên các thư mục cha (Tối đa 5 cấp để tránh treo lặp vô tận)
            int maxDepth = 5;
            while (currentDir != null && maxDepth > 0)
            {
                string potentialPath = Path.Combine(currentDir.FullName, "AI_Backend");
                if (Directory.Exists(potentialPath))
                {
                    backendFolder = potentialPath;
                    break;
                }
                currentDir = currentDir.Parent;
                maxDepth--;
            }

            if (backendFolder != null)
            {
                // Tìm thấy AI_Backend! Bắt đầu ráp nối vũ khí
                PYTHON_EXE_PATH = Path.Combine(backendFolder, "env", "python.exe");
                PYTHON_SCRIPT_PATH = Path.Combine(backendFolder, "main.py");

                // Mẹo nhỏ: Python chuẩn trên Windows thường để file chạy trong env\Scripts\python.exe
                // Nếu đường dẫn env\python.exe của bạn bị lỗi, hệ thống sẽ tự fallback sang chuẩn Scripts
                if (!File.Exists(PYTHON_EXE_PATH))
                {
                    PYTHON_EXE_PATH = Path.Combine(backendFolder, "env", "Scripts", "python.exe");
                }
            }
        }

        public static async Task<string> CallPythonBackendAsync(string query, string sessionId, string commandType, string contextCode = "", string specText = "", string targetType = "AUTO", string userTags = "")
        {
            try
            {
                ProcessStartInfo start = new ProcessStartInfo();
                start.FileName = PYTHON_EXE_PATH;
                start.Arguments = $"\"{PYTHON_SCRIPT_PATH}\"";
                start.UseShellExecute = false;
                start.RedirectStandardInput = true;
                start.RedirectStandardOutput = true;
                start.RedirectStandardError = true;
                start.CreateNoWindow = true;
                start.StandardOutputEncoding = Encoding.UTF8;
                start.WorkingDirectory = Path.GetDirectoryName(PYTHON_SCRIPT_PATH);

                using (Process process = Process.Start(start))
                {
                    var payload = new
                    {
                        query = query,
                        session_id = sessionId,
                        command = commandType,
                        context_code = contextCode,
                        spec_text = specText,
                        target_block_type = targetType,
                        user_tags = userTags
                    };

                    string jsonInput = JsonConvert.SerializeObject(payload);

                    // 1. [KHẮC PHỤC BOM & FLUSH]
                    // Dùng UTF8Encoding(false) để tắt BOM, ngăn Python bị ngáo JSON
                    using (StreamWriter writer = new StreamWriter(process.StandardInput.BaseStream, new UTF8Encoding(false)))
                    {
                        await writer.WriteAsync(jsonInput);
                        await writer.FlushAsync(); // ÉP XẢ BỘ ĐỆM NGAY LẬP TỨC
                    }

                    // 2. TUYỆT KỸ PHÁ DEADLOCK: Đọc 2 luồng CÙNG MỘT LÚC!
                    var outputTask = process.StandardOutput.ReadToEndAsync();
                    var errorTask = process.StandardError.ReadToEndAsync();
                    
                    await Task.WhenAll(outputTask, errorTask); // Chờ cả 2 đọc xong

                    // 3. [KHẮC PHỤC DOUBLE READ]
                    // Lấy kết quả trực tiếp từ cái phễu đã hứng (Task.Result), KHÔNG đọc lại luồng nữa!
                    string result = outputTask.Result;
                    string error = errorTask.Result;

                    await Task.Run(() => process.WaitForExit());

                    // Kiểm tra lỗi nếu Error có chữ mà Result trống trơn
                    if (!string.IsNullOrEmpty(error) && string.IsNullOrWhiteSpace(result))
                    {
                        return JsonConvert.SerializeObject(new { status = "error", message = error });
                    }
                    return result;
                }
            }
            catch (Exception ex)
            {
                return JsonConvert.SerializeObject(new { status = "error", message = ex.Message });
            }
        }
    }
}