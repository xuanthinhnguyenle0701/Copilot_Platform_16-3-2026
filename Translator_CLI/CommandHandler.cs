using System;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Text.RegularExpressions;
using System.Text;
using System.Diagnostics;

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

            if (string.IsNullOrWhiteSpace(jsonResponse))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("\n[LỖI NGHIÊM TRỌNG]: Python không trả về bất kỳ dữ liệu nào (Chuỗi rỗng)!");
                Console.ResetColor();
            }
            else
            {
                Console.WriteLine($"[DEBUG] Đã nhận {jsonResponse.Length} ký tự từ Python."); // Bật dòng này nếu muốn soi độ dài
            }
            ProcessResponse(jsonResponse);
            Console.WriteLine($"\n✅ [DONE] Quá trình sinh code hoàn tất!\n");
        }

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

        public static async Task HandleClearDataAsync(string sessionId)
        {
            // --- LỚP KHIÊN CẢNH BÁO ---
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"⚠️ [CẢNH BÁO MỨC ĐỘ CAO] Bạn sắp xóa toàn bộ tri thức (Spec & Tags) của Session: [{sessionId.ToUpper()}]");
            
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.Write("Hành động này KHÔNG THỂ hoàn tác. Bạn có chắc chắn muốn bóp cò? (y/n): ");
            Console.ResetColor();

            string confirm = Console.ReadLine()?.Trim().ToLower();
            if (confirm != "y" && confirm != "yes")
            {
                Program.PrintIcon("i", "Đã hủy thao tác dọn dẹp. Dữ liệu vẫn an toàn.", ConsoleColor.DarkGray);
                return;
            }

            // --- BẮT ĐẦU XÓA ---
            Console.WriteLine($"\n🚀 [START] Tiến hành dọn dẹp hệ thống...");
            
            // Đấm 1: Xóa file Tag Cache
            if (File.Exists(TagCacheFile))
            {
                try 
                {
                    File.Delete(TagCacheFile);
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("[SUCCESS] Đã xóa toàn bộ I/O Tags khỏi Cache.");
                }
                catch (Exception ex)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"[ERROR] Không thể xóa Tag Cache: {ex.Message}");
                }
                Console.ResetColor();
            }

            // Đấm 2: Xóa Vector DB
            var backendTask = AiEngine.CallPythonBackendAsync("", sessionId, "clear_spec");
            string jsonResponse = await RunWithSpinner(backendTask, "Đang dọn dẹp Vector DB...");

            try
            {
                dynamic obj = JsonConvert.DeserializeObject(jsonResponse);
                if (obj.status == "success") 
                { 
                    Console.ForegroundColor = ConsoleColor.Green; 
                    Console.WriteLine($"[SUCCESS] {obj.message}"); 
                }
                else 
                { 
                    Console.ForegroundColor = ConsoleColor.Red; 
                    Console.WriteLine($"[ERROR] {obj.message}"); 
                }
            }
            catch 
            { 
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("[ERROR] Lỗi parse phản hồi từ Python Backend."); 
            }
            
            Console.ResetColor();
        }

        public static async Task HandleCheckStatusAsync(string sessionId)
        {
            // 1. Quét I/O Tags (Bộ nhớ cục bộ C#)
            string tagStatus = "❌ CHƯA NẠP (Trống)";
            string tagFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tags_cache.txt"); 
            
            if (File.Exists(tagFilePath))
            {
                FileInfo fi = new FileInfo(tagFilePath);
                tagStatus = $"✅ ĐÃ NẠP | Size: {fi.Length / 1024} KB | Cập nhật: {fi.LastWriteTime:dd/MM/yyyy HH:mm}";
            }

            // 2. Quét System Spec (Gọi sang Vector DB của Python)
            string specStatus = "ĐANG TRUY VẤN...";
            try
            {
                // Gọi API backend với Spinner
                var backendTask = AiEngine.CallPythonBackendAsync("", sessionId, "check_spec");
                string jsonResponse = await RunWithSpinner(backendTask, $"Đang quét Radar Vector DB cho Session [{sessionId.ToUpper()}]...");

                dynamic obj = JsonConvert.DeserializeObject(jsonResponse);
                if (obj.status == "success")
                {
                    string msg = obj.message.ToString();
                    
                    // BỘ LỌC THÔNG MINH: Đọc hiểu câu trả lời của Python
                    if (msg.Contains("No current spec found") || msg.Contains("empty"))
                    {
                        specStatus = "❌ CHƯA NẠP (Trống)";
                    }
                    else
                    {
                        // Nếu có Spec, Python sẽ trả về 1 cục text rất dài. Ta chỉ lấy dòng đầu tiên in ra cho gọn!
                        // VD: "Found 15 chunks in current Spec."
                        string briefMsg = msg.Split('\n')[0]; 
                        specStatus = $"✅ ĐÃ NẠP | {briefMsg} | Trạng thái: Sẵn sàng";
                    }
                }
                else
                {
                    specStatus = $"❌ LỖI TỪ PYTHON: {obj.message}";
                }
            }
            catch (Exception)
            {
                specStatus = "❌ LỖI KẾT NỐI PYTHON BACKEND";
            }

            // 3. DỌN SẠCH MÀN HÌNH (XÓA SPINNER) & VẼ UI DASHBOARD
            Console.Clear();
            Console.WriteLine("\n" + new string('=', 70));
            Console.ForegroundColor = ConsoleColor.Magenta;
            Console.WriteLine($" 📊 TRẠNG THÁI DỮ LIỆU - SESSION: {sessionId.ToUpper()}");
            Console.ResetColor();
            Console.WriteLine(new string('=', 70));

            Console.Write(" [1] I/O Tags (Cache)   : ");
            Console.ForegroundColor = tagStatus.Contains("✅") ? ConsoleColor.Green : ConsoleColor.DarkGray;
            Console.WriteLine(tagStatus);
            Console.ResetColor();

            Console.Write(" [2] System Spec (RAG)  : ");
            Console.ForegroundColor = specStatus.Contains("✅") ? ConsoleColor.Green : ConsoleColor.DarkGray;
            Console.WriteLine(specStatus);
            Console.ResetColor();

            Console.WriteLine(new string('=', 70) + "\n");
        }

        public static async Task HandleSessionMenuAsync()
        {
            bool keepMenuOpen = true;

            while (keepMenuOpen)
            {
                // =========================================================
                // 1. GỌI PYTHON LẤY DỮ LIỆU (CHẠY NGẦM VỚI SPINNER TRƯỚC KHI VẼ UI)
                // =========================================================
                List<string> dbSessions = new List<string>();
                try
                {
                    var backendTask = AiEngine.CallPythonBackendAsync("", Program._currentSessionId, "list_sessions");
                    
                    // Spinner sẽ xoay ở màn hình cũ, tải xong sẽ bị Console.Clear() quét sạch!
                    string jsonRes = await RunWithSpinner(backendTask, "Đang đồng bộ danh sách Session...", 15);
                    dynamic obj = JsonConvert.DeserializeObject(jsonRes);
                    if (obj != null && obj.status == "success" && obj.sessions != null)
                    {
                        foreach (var s in obj.sessions) dbSessions.Add((string)s);
                    }
                }
                catch { /* Lỗi âm thầm bỏ qua, dùng list rỗng */ }

                // Ép luôn luôn phải có default và session hiện tại để UI không bị vỡ
                if (!dbSessions.Contains("default")) dbSessions.Insert(0, "default");
                if (!dbSessions.Contains(Program._currentSessionId)) dbSessions.Add(Program._currentSessionId);

                // =========================================================
                // 2. XÓA MÀN HÌNH VÀ VẼ GIAO DIỆN SẠCH TƯNG
                // =========================================================
                Console.Clear();
                Console.ForegroundColor = ConsoleColor.Magenta;
                Console.WriteLine("==========================================================");
                Console.WriteLine(" 📂 TRUNG TÂM CHỈ HUY SESSION (CHAT & CONTEXT)");
                Console.WriteLine("==========================================================");
                Console.ResetColor();

                Console.WriteLine($"\n Session hiện tại: [{Program._currentSessionId.ToUpper()}]\n");

                // --- IN DANH SÁCH ---
                for (int i = 0; i < dbSessions.Count; i++)
                {
                    if (dbSessions[i] == Program._currentSessionId)
                    {
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine($"  [{i + 1}] -> {dbSessions[i]} (Active)");
                        Console.ResetColor();
                    }
                    else
                    {
                        Console.WriteLine($"  [{i + 1}]    {dbSessions[i]}");
                    }
                }

                Console.WriteLine("\n----------------------------------------------------------");
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine(" [SỐ]: Chọn | [C]: Tạo mới | [S]: Xóa Session | [H]: Xóa Lịch sử | [ESC]: Thoát");
                Console.ResetColor();

                // --- 3. BẮT SỰ KIỆN BÀN PHÍM ---
                var keyInfo = Console.ReadKey(intercept: true);
                char key = char.ToUpper(keyInfo.KeyChar);

                if (keyInfo.Key == ConsoleKey.Escape)
                {
                    keepMenuOpen = false;
                    Console.Clear(); // Trả lại màn hình gõ lệnh
                }
                // TÍNH NĂNG [C]: TẠO SESSION CÓ SPINNER
                else if (key == 'C')
                {
                    Console.WriteLine("\n");
                    Console.Write(" >> Nhập tên Session mới (Viết liền không dấu): ");
                    string newSession = Console.ReadLine()?.Trim().Replace(" ", "_").ToLower();

                    if (!string.IsNullOrEmpty(newSession) && !dbSessions.Contains(newSession))
                    {
                        // GẮN SPINNER TẠO MỚI
                        var createTask = AiEngine.CallPythonBackendAsync("", newSession, "create_session");
                        await RunWithSpinner(createTask, $"Đang khởi tạo không gian cho [{newSession}]...");
                        
                        Program._currentSessionId = newSession;
                        Program.PrintIcon("√", $"Đã tạo và chuyển sang Session: {newSession}", ConsoleColor.Green);
                        await Task.Delay(1000);
                    }
                }
                // TÍNH NĂNG [S] & [H]: XÓA DỮ LIỆU CÓ SPINNER
                else if (key == 'S' || key == 'H')
                {
                    string actionName = key == 'S' ? "TIÊU DIỆT SESSION" : "XÓA LỊCH SỬ CHAT";
                    Console.WriteLine($"\n\n 👉 Bạn chọn [{actionName}].");
                    Console.Write(" >> Nhập SỐ thứ tự Session muốn thao tác (Bấm Enter để Hủy): ");
                    
                    if (int.TryParse(Console.ReadLine(), out int targetIdx) && targetIdx > 0 && targetIdx <= dbSessions.Count)
                    {
                        string targetSession = dbSessions[targetIdx - 1];

                        if (key == 'S' && targetSession == "default") 
                        {
                            Program.PrintIcon("×", "TỪ CHỐI: Không được tiêu diệt Session gốc 'default'.", ConsoleColor.Red);
                            await Task.Delay(1500);
                            continue;
                        }

                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.Write($" ⚠️ CẢNH BÁO: Xác nhận [{actionName}] với '{targetSession}'? (y/n): ");
                        Console.ResetColor();
                        
                        string confirm = Console.ReadLine()?.Trim().ToLower();
                        if (confirm == "y" || confirm == "yes")
                        {
                            // GẮN SPINNER XÓA DỮ LIỆU
                            var resetTask = AiEngine.CallPythonBackendAsync("", targetSession, "reset");
                            await RunWithSpinner(resetTask, $"Đang tiến hành dọn dẹp [{targetSession}]...");
                            
                            if (key == 'S')
                            {
                                Program.PrintIcon("√", $"Đã tiêu diệt hoàn toàn Session: {targetSession}", ConsoleColor.Green);
                                if (Program._currentSessionId == targetSession)
                                {
                                    Program._currentSessionId = "default";
                                    Program.PrintIcon("i", "Đã tự động chuyển về Session 'default'.", ConsoleColor.Cyan);
                                }
                            }
                            else
                            {
                                // GẮN SPINNER TẠO LẠI VỎ SESSION MỚI (CHỈ MẤT LỊCH SỬ)
                                var recreateTask = AiEngine.CallPythonBackendAsync("", targetSession, "create_session");
                                await RunWithSpinner(recreateTask, $"Đang thiết lập lại vỏ Session [{targetSession}]...");
                                Program.PrintIcon("√", $"Đã xóa sạch bộ nhớ lịch sử chat của: {targetSession}", ConsoleColor.Green);
                            }
                            await Task.Delay(1500);
                        }
                    }
                }
                // TÍNH NĂNG [SỐ]: CHỌN SESSION
                else if (int.TryParse(key.ToString(), out int selection) && selection > 0 && selection <= dbSessions.Count)
                {
                    Program._currentSessionId = dbSessions[selection - 1];
                    Program.PrintIcon("√", $"Đã chuyển sang Session: {Program._currentSessionId}", ConsoleColor.Green);
                    await Task.Delay(800);
                }
            }
        }
        
        public static async Task HandleCheckDataAsync(string sessionId)
        {
            Console.WriteLine();
            
            // Dùng StringBuilder để đúc một file text hoàn chỉnh
            StringBuilder dumpData = new StringBuilder();
            dumpData.AppendLine("==========================================================");
            dumpData.AppendLine($" 📂 TIA COPILOT - DỮ LIỆU BỐI CẢNH (SESSION: {sessionId.ToUpper()})");
            dumpData.AppendLine($" 🕒 Thời gian trích xuất: {DateTime.Now:dd/MM/yyyy HH:mm:ss}");
            dumpData.AppendLine("==========================================================\n");

            // --- 1. LẤY I/O TAGS CACHE (LOCAL C#) ---
            dumpData.AppendLine("--- [1] I/O TAGS CACHE (LOCAL) ---");
            string tagFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tags_cache.txt");
            if (File.Exists(tagFilePath))
            {
                string tagsContent = File.ReadAllText(tagFilePath);
                dumpData.AppendLine(tagsContent.TrimStart('\uFEFF')); // Gọt ký tự BOM nếu có
            }
            else
            {
                dumpData.AppendLine("(Trống - Chưa nạp I/O Tags nào)");
            }
            dumpData.AppendLine("\n");

            // --- 2. LẤY SYSTEM SPEC (VECTOR DB PYTHON) ---
            dumpData.AppendLine("--- [2] SYSTEM SPEC (VECTOR DB) ---");
            try
            {
                var backendTask = AiEngine.CallPythonBackendAsync("", sessionId, "check_spec");
                string jsonResponse = await RunWithSpinner(backendTask, $"Đang gom dữ liệu từ Vector DB cho Session [{sessionId.ToUpper()}]...", 20);

                dynamic obj = JsonConvert.DeserializeObject(jsonResponse);
                if (obj.status == "success")
                {
                    string msg = obj.message.ToString();
                    if (msg.Contains("No current spec found") || msg.Contains("empty"))
                    {
                        dumpData.AppendLine("(Trống - Hệ thống chưa nạp tài liệu Spec nào)");
                    }
                    else
                    {
                        dumpData.AppendLine(msg); // In toàn bộ nội dung Spec dài dằng dặc ra đây
                    }
                }
                else
                {
                    dumpData.AppendLine($"[LỖI TỪ PYTHON]: {obj.message}");
                }
            }
            catch (Exception ex)
            {
                dumpData.AppendLine($"[LỖI KẾT NỐI PYTHON BACKEND]: {ex.Message}");
            }

            // --- 3. XUẤT FILE VÀ GỌI NOTEPAD ---
            try
            {
                string exportFileName = "TIA_Copilot_Context_Dump.txt";
                string exportPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, exportFileName);
                
                File.WriteAllText(exportPath, dumpData.ToString(), Encoding.UTF8);

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"\n[√] Đã trích xuất thành công ra file: {exportFileName}");
                Console.ResetColor();
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine($"📁 Đường dẫn: {exportPath}\n");
                Console.ResetColor();

                // Lệnh bọc thép gọi hệ điều hành Windows mở file bằng Notepad mặc định
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = exportPath,
                    UseShellExecute = true // BẮT BUỘC = true để Windows tự chọn app mở file .txt
                };
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"\n[×] Không thể tự động mở Notepad. Lỗi OS: {ex.Message}");
                Console.ResetColor();
                Console.WriteLine("👉 Bạn hãy mở thủ công theo đường dẫn trên.");
            }
        }
        
        public static async Task<T> RunWithSpinner<T>(Task<T> targetTask, string waitingMessage, int timeoutSeconds = 180)
        {
            char[] spinnerChars = new char[] { '|', '/', '-', '\\' };
            int spinnerIndex = 0;

            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.Write($"{waitingMessage}  ");

            // Lưu lại vị trí con trỏ chuột hiện tại để ghi đè ký tự xoay
            int cursorLeft = Console.CursorLeft;
            int cursorTop = Console.CursorTop;

            // 1. Lên cò mốc thời gian nổ (Timeout)
            DateTime timeoutTime = DateTime.Now.AddSeconds(timeoutSeconds);

            try
            {
                // 2. Vòng lặp Spinner: Xoay cho đến khi targetTask xong HOẶC hết giờ
                while (!targetTask.IsCompleted)
                {
                    // KHIÊN CHỐNG TREO: Chặt đứt vòng lặp nếu quá hạn
                    if (DateTime.Now > timeoutTime)
                    {
                        throw new TimeoutException($"Backend không phản hồi sau {timeoutSeconds} giây.");
                    }

                    Console.SetCursorPosition(cursorLeft - 1, cursorTop);
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.Write(spinnerChars[spinnerIndex]);
                    spinnerIndex = (spinnerIndex + 1) % spinnerChars.Length;

                    // Tránh giật lag CPU, xoay mỗi 100ms
                    await Task.Delay(100);
                }

                // 3. Trả về kết quả thực sự của backend nếu mọi thứ suôn sẻ
                return await targetTask; 
            }
            catch (TimeoutException ex)
            {
                // 4. CHIẾN THUẬT NGHI BINH: Nếu kiểu trả về T là chuỗi (string)
                // Ta nặn ra một cục JSON giả mạo để lừa các hàm phía sau không bị Crash
                if (typeof(T) == typeof(string))
                {
                    string fakeJson = Newtonsoft.Json.JsonConvert.SerializeObject(new 
                    { 
                        status = "error", 
                        message = $"TIMEOUT CRASH: {ex.Message} Hãy kiểm tra lại Server Python!" 
                    });
                    return (T)(object)fakeJson; // Ép kiểu an toàn về T
                }
                throw; // Nếu T không phải string (VD: là int, bool), đành quăng lỗi ra ngoài
            }
            catch (Exception ex)
            {
                if (typeof(T) == typeof(string))
                {
                    string fakeJson = Newtonsoft.Json.JsonConvert.SerializeObject(new 
                    { 
                        status = "error", 
                        message = $"LỖI KẾT NỐI: {ex.Message}" 
                    });
                    return (T)(object)fakeJson;
                }
                throw;
            }
            finally
            {
                // 5. DỌN CHIẾN TRƯỜNG (LUÔN CHẠY DÙ CÓ LỖI HAY KHÔNG)
                Console.SetCursorPosition(cursorLeft - 1, cursorTop);
                Console.Write(" "); // Xóa ký tự spinner
                Console.SetCursorPosition(cursorLeft - 1, cursorTop);
                Console.WriteLine(); // Xuống dòng trả lại giao diện bình thường
                Console.ResetColor();
            }
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