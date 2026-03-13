using System;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Text.RegularExpressions;
using System.Text;

namespace TIA_Copilot_CLI
{
    public static class CommandHandler
    {
        public static string DefaultSessionID = "default_session";
        
        // ---> [MỚI]: Khai báo vị trí file lưu tạm (Cache) nằm ngay cạnh file .exe
        private static readonly string TagCacheFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tags_cache.txt");

        public static string GetBlockType(string input)
        {
            string up = input.ToUpper();
            if (up == "OB" || up == "ORGANIZATION_BLOCK") return "ORGANIZATION_BLOCK";
            if (up == "FB" || up == "FUNCTION_BLOCK") return "FUNCTION_BLOCK";
            if (up == "FC" || up == "FUNCTION") return "FUNCTION";
            return "AUTO";
        }
        // =================================================================
        // 1. LỆNH NẠP TAGS (Lưu vào ổ cứng)
        // =================================================================
        public static async Task HandleLoadTagsAsync(string tagFilePath)
        {
            Console.WriteLine($"\n🚀 [START] Bắt đầu nạp I/O Tags từ: {tagFilePath}");
            
            if (string.IsNullOrEmpty(tagFilePath) || !File.Exists(tagFilePath))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[ERROR] Không tìm thấy file Tag. Vui lòng kiểm tra lại đường dẫn.");
                Console.ResetColor();
                return;
            }

            string userTagsContent = "";
            string ext = Path.GetExtension(tagFilePath).ToLower();
            
            if (ext == ".xlsx" || ext == ".xls") userTagsContent = TagManager.ReadUserTagsExcel(tagFilePath);
            else if (ext == ".csv") userTagsContent = TagManager.ReadUserTagsCsv(tagFilePath);

            if (!string.IsNullOrEmpty(userTagsContent))
            {
                // Ghi đè chuỗi Tag đã gọt sạch sẽ vào file txt ẩn
                File.WriteAllText(TagCacheFile, userTagsContent);
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"[SUCCESS] Đã lưu Tags vào bộ nhớ Cache cục bộ thành công!");
                Console.ResetColor();
            }
        }

        // =================================================================
        // 2. LỆNH CHAT & SINH CODE (Đọc từ ổ cứng)
        // =================================================================
        public static async Task HandleChatAsync(string targetType, string query, string sessionId)
        {
            Console.WriteLine($"\n🚀 [START] Bắt đầu sinh code cho khối: {targetType}");
            
            string userTagsContent = "";

            // Nếu là OB, tự động đi mò file Cache xem có tồn tại không
            if (targetType == "ORGANIZATION_BLOCK")
            {
                if (File.Exists(TagCacheFile))
                {
                    byte[] fileBytes = File.ReadAllBytes(TagCacheFile);
                    string content = Encoding.UTF8.GetString(fileBytes);
                    userTagsContent = content.TrimStart('\uFEFF');
                    Console.ForegroundColor = ConsoleColor.Magenta;
                    Console.WriteLine($"[SYSTEM] Đã tìm thấy I/O Tags trong Cache. Tiến hành đính kèm vào Prompt.");
                    Console.ResetColor();
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"[CẢNH BÁO] Không tìm thấy dữ liệu Tag Cache. (Bạn chưa chạy lệnh load-tags). AI sẽ tự sinh Tag mới.");
                    Console.ResetColor();
                }
            }

            // Gọi backend
            var backendTask = AiEngine.CallPythonBackendAsync(query, sessionId, "chat", "", "", targetType, userTagsContent);
            string jsonResponse = await RunWithSpinner(backendTask, "AI đang suy nghĩ và phân tích logic...");

            ProcessResponse(jsonResponse);
            Console.WriteLine($"\n✅ [DONE] Quá trình sinh code hoàn tất!\n");
        }

        // =================================================================
        // 3. LỆNH NẠP SPEC (Giữ nguyên)
        // =================================================================
        public static async Task HandleLoadSpecAsync(string specPath, string sessionId)
        {
            // ... (Copy nguyên ruột hàm HandleLoadSpecAsync từ phiên bản trước vào đây) ...
            Console.WriteLine($"\n🚀 [START] Nạp Yêu cầu vận hành vào Vector DB...");
            if (!File.Exists(specPath))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[ERROR] Không tìm thấy file Spec: {specPath}");
                Console.ResetColor();
                return;
            }
            string rawSpec = File.ReadAllText(specPath, Encoding.UTF8);
            string specText = rawSpec.TrimStart('\uFEFF');
            var backendTask = AiEngine.CallPythonBackendAsync("", sessionId, "update_spec", "", specText);
            string jsonResponse = await RunWithSpinner(backendTask, "Đang băm nhỏ file và nạp vào ChromaDB...");

            try
            {
                dynamic obj = JsonConvert.DeserializeObject(jsonResponse);
                if (obj.status == "success") { Console.ForegroundColor = ConsoleColor.Green; Console.WriteLine($"[SUCCESS] {obj.message}"); }
                else { Console.ForegroundColor = ConsoleColor.Red; Console.WriteLine($"[ERROR] {obj.message}"); }
            }
            catch { Console.WriteLine("[ERROR] Lỗi parse phản hồi từ Python Backend."); }
            Console.ResetColor();
        }

        // =================================================================
        // 4. LỆNH DỌN DẸP CACHE VÀ DATABASE (CLEAR DATA)
        // =================================================================
        public static async Task HandleClearDataAsync(string sessionId)
        {
            Console.WriteLine($"\n🚀 [START] Tiến hành dọn dẹp hệ thống...");
            
            // Đấm 1: Xóa file Tag Cache
            if (File.Exists(TagCacheFile))
            {
                File.Delete(TagCacheFile);
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("[SUCCESS] Đã xóa toàn bộ I/O Tags khỏi Cache.");
                Console.ResetColor();
            }

            // Đấm 2: Xóa Vector DB
            var backendTask = AiEngine.CallPythonBackendAsync("", sessionId, "clear_spec");
            string jsonResponse = await RunWithSpinner(backendTask, "Đang dọn dẹp Vector DB...");

            try
            {
                dynamic obj = JsonConvert.DeserializeObject(jsonResponse);
                if (obj.status == "success") { Console.ForegroundColor = ConsoleColor.Green; Console.WriteLine($"[SUCCESS] {obj.message}"); }
                else { Console.ForegroundColor = ConsoleColor.Red; Console.WriteLine($"[ERROR] {obj.message}"); }
            }
            catch { Console.WriteLine("[ERROR] Lỗi parse phản hồi từ Python Backend."); }
            
            Console.ResetColor();
        }

        public static async Task<T> RunWithSpinner<T>(Task<T> targetTask, string waitingMessage)
        {
            char[] spinnerChars = new char[] { '|', '/', '-', '\\' };
            int spinnerIndex = 0;

            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.Write($"{waitingMessage}  ");

            // Lưu lại vị trí con trỏ chuột hiện tại để ghi đè ký tự xoay
            int cursorLeft = Console.CursorLeft;
            int cursorTop = Console.CursorTop;

            // Vòng lặp Spinner: Xoay liên tục cho đến khi targetTask chạy xong
            while (!targetTask.IsCompleted)
            {
                Console.SetCursorPosition(cursorLeft - 1, cursorTop);
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Write(spinnerChars[spinnerIndex]);
                spinnerIndex = (spinnerIndex + 1) % spinnerChars.Length;

                // Tránh giật lag CPU, xoay mỗi 100ms
                await Task.Delay(100);
            }

            // Dọn dẹp Spinner khi xong
            Console.SetCursorPosition(cursorLeft - 1, cursorTop);
            Console.Write(" "); // Xóa ký tự spinner
            Console.SetCursorPosition(cursorLeft - 1, cursorTop);
            Console.WriteLine(); // Xuống dòng trả lại giao diện bình thường
            Console.ResetColor();

            return await targetTask; // Trả về kết quả thực sự của backend
        }

        public static void ProcessResponse(string jsonResponse)
        {
            if (!string.IsNullOrEmpty(jsonResponse))
            {
                try
                {
                    // =========================================================
                    // 🛡️ [BỌC THÉP LỚP 1]: GỌT SẠCH RÁC TRƯỚC VÀ SAU JSON
                    // Bắt đầu gọt từ dấu ngoặc nhọn đầu tiên đến dấu cuối cùng
                    // =========================================================
                    int startIndex = jsonResponse.IndexOf('{');
                    int endIndex = jsonResponse.LastIndexOf('}');

                    if (startIndex != -1 && endIndex != -1 && endIndex > startIndex)
                    {
                        // Cắt lấy đúng phần lõi, mọi dấu phẩy hay text dư bên ngoài sẽ bị vứt bỏ
                        jsonResponse = jsonResponse.Substring(startIndex, endIndex - startIndex + 1);
                    }

                    jsonResponse = Regex.Replace(jsonResponse, @"\}\s*,\s*""global_tags""", @", ""global_tags""");
                    jsonResponse = Regex.Replace(jsonResponse, @"\}\s*""global_tags""", @", ""global_tags""");

                    JObject responseObj = JObject.Parse(jsonResponse);

                    if (responseObj.ContainsKey("token_usage"))
                    {
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine($"\n⚡ [TOKEN MONITOR] Prompt's token amount: {responseObj["token_usage"]} tokens");
                        Console.ResetColor();
                    }

                    if (responseObj.ContainsKey("status") && responseObj["status"].ToString() == "error")
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine($"[AI ERROR]: {responseObj["message"]}");
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine($"Raw JSON: {jsonResponse}");
                        Console.ResetColor();
                    }
                    else
                    {
                        var standardizedData = DataNormalizer.Normalize(responseObj);
                        //DisplayResult(standardizedData);
                        SCLGenerator.GenerateAndSave(standardizedData);
                    }
                }
                catch (Exception ex)
                {
                    // =========================================================
                    // 🛡️ [BỌC THÉP LỚP 2]: HIỂN THỊ RÁC ĐỂ KHÁM NGHIỆM
                    // =========================================================
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"\n[LỖI C# PARSE JSON]: {ex.Message}");

                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.WriteLine("\n--- RAW DATA TỪ PYTHON GỬI VỀ ---");
                    Console.WriteLine(jsonResponse);
                    Console.WriteLine("---------------------------------");
                    Console.ResetColor();
                }
            }
        }
    }
}