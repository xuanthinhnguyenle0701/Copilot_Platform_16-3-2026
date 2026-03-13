using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using System.Linq;
using Newtonsoft.Json;
using Middleware_console;
using System.Windows.Forms;
using System.Threading;
using System.Collections.Generic;

namespace TIA_Copilot_CLI
{
    class Program
    {
        private static TIA_V20 _tiaEngine = new TIA_V20();
        private static string _currentProjectName = "None";
        private static string _currentDeviceName = "None";
        private static string _currentDeviceType = "None";
        private static string _currentIp = "0.0.0.0";
        private static string _lastGeneratedFilePath = "";

        [STAThread] // Bắt buộc để chạy OpenFileDialog
        static async Task Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            Console.InputEncoding = Encoding.UTF8;

            AiEngine.InitializePaths();

            if (!File.Exists(AiEngine.PYTHON_EXE_PATH) || !File.Exists(AiEngine.PYTHON_SCRIPT_PATH))
            {
                PrintIcon("!", "LỖI CẤU HÌNH: Không tìm thấy thư mục AI_Backend!", ConsoleColor.Red);
                return;
            }

            if (args.Length > 0)
            {
                await RouteCommand(args);
            }
            else
            {
                await RunInteractiveShell();
            }
        }

        static async Task RunInteractiveShell()
        {
            string userName = Environment.UserName;
            string appName = "TIACopilot";

            Console.Clear();
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("==========================================================");
            Console.WriteLine($" Chào mừng đến với {appName} Interactive Shell!");
            Console.WriteLine(" Gõ lệnh, bấm phím [ESC] để thoát, hoặc gõ 'help' để xem.");
            Console.WriteLine("==========================================================\n");
            Console.ResetColor();

            while (true)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.Write($"{userName}@{appName}");
                Console.ResetColor();
                Console.Write(" > ");

                string input = ReadLineWithEscape();

                if (input == null || input.Trim().ToLower() == "exit")
                {
                    PrintIcon("!", "Đã nhận lệnh thoát. Đang đóng engine...", ConsoleColor.Yellow);
                    break;
                }

                if (string.IsNullOrWhiteSpace(input)) continue;

                string[] cmdArgs = Regex.Matches(input, @"[\""].+?[\""]|[^ ]+")
                                        .Cast<Match>().Select(m => m.Value.Trim('"'))
                                        .ToArray();

                await RouteCommand(cmdArgs);
            }
        }

        static async Task RouteCommand(string[] args)
        {
            if (args.Length == 0) return;
            string command = args[0].ToLower();
            string sessionId = CommandHandler.DefaultSessionID;

            try
            {
                if (command == "tia")
                {
                    HandleTiaCommand(args);
                    return;
                }

                switch (command)
                {
                    case "chat":
                        string targetType = args.Length > 1 ? CommandHandler.GetBlockType(args[1]) : "AUTO";
                        string query = args.Length > 2 ? args[2] : "";
                        if (args.Length > 3) sessionId = args[3];
                        if (string.IsNullOrEmpty(query)) { Console.WriteLine("LỖI: Thiếu query."); return; }
                        await CommandHandler.HandleChatAsync(targetType, query, sessionId);
                        break;
                    case "load-tags":
                        await CommandHandler.HandleLoadTagsAsync(args.Length > 1 ? args[1] : "");
                        break;
                    case "help":
                        PrintHelp();
                        break;
                    default:
                        PrintIcon("?", $"Không tìm thấy lệnh '{command}'. Gõ 'help' để xem.", ConsoleColor.Yellow);
                        break;
                }
            }
            catch (Exception ex) { PrintIcon("×", $"Lỗi: {ex.Message}", ConsoleColor.Red); }
        }

        public static void HandleTiaCommand(string[] args)
        {
            if (args.Length < 2)
            {
                PrintIcon("!", "Cần nhập action (VD: tia connect, tia open...)", ConsoleColor.Yellow);
                return;
            }

            string action = args[1].ToLower();

            switch (action)
            {
                case "connect":
                    PrintIcon("i", "Đang kết nối TIA Portal...", ConsoleColor.Cyan);
                    if (_tiaEngine.ConnectToTIA()) {
                        _currentProjectName = _tiaEngine.GetProjectName();
                        PrintIcon("√", $"Đã kết nối: {_currentProjectName}", ConsoleColor.Green);
                    } else PrintIcon("×", "Không thấy TIA Portal đang chạy.", ConsoleColor.Red);
                    break;

                case "open":
                    string openPath = GetPathOrOpenDialog(args, 2, "TIA Project (*.ap*)|*.ap*");
                    if (!string.IsNullOrEmpty(openPath)) {
                        PrintIcon("i", $"Mở dự án: {Path.GetFileName(openPath)}...", ConsoleColor.Cyan);
                        if (_tiaEngine.CreateTIAproject(openPath, "", false)) {
                            _currentProjectName = Path.GetFileNameWithoutExtension(openPath);
                            PrintIcon("√", $"Đã mở: {_currentProjectName}", ConsoleColor.Green);
                        }
                    }
                    break;

                case "save":
                    PrintIcon("i", "Đang lưu project...", ConsoleColor.Cyan);
                    _tiaEngine.SaveProject();
                    PrintIcon("√", "Lưu thành công.", ConsoleColor.Green);
                    break;

                case "close":
                    _tiaEngine.CloseTIA();
                    _currentProjectName = "None";
                    PrintIcon("√", "Đã đóng TIA.", ConsoleColor.DarkGray);
                    break;

                case "choose":
                    HandleChooseDevice(args);
                    break;

                case "fb": case "fc": case "ob":
                    string sclPath = GetPathOrOpenDialog(args, 2, "SCL Files (*.scl)|*.scl");
                    TiaImportLogic(action.ToUpper(), sclPath);
                    break;

                case "tag-plc":
                    string pTagPath = GetPathOrOpenDialog(args, 2, "CSV Tags (*.csv)|*.csv");
                    if (!string.IsNullOrEmpty(pTagPath)) _tiaEngine.ImportPlcTagsFromCsv(_currentDeviceName, pTagPath);
                    break;

                case "tag-hmi":
                    string hTagPath = GetPathOrOpenDialog(args, 2, "CSV Tags (*.csv)|*.csv");
                    if (!string.IsNullOrEmpty(hTagPath)) _tiaEngine.ImportHmiTagsFromCsv("PC-System_1", hTagPath);
                    break;

                case "draw":
                    string jPath = GetPathOrOpenDialog(args, 2, "JSON SCADA (*.json)|*.json");
                    if (!string.IsNullOrEmpty(jPath)) {
                        var data = JsonConvert.DeserializeObject<ScadaScreenModel>(File.ReadAllText(jPath));
                        _tiaEngine.CreateUnifiedScreen("PC-System_1", data.ScreenName);
                        _tiaEngine.GenerateScadaScreenFromData("PC-System_1", data);
                        PrintIcon("√", $"Đã vẽ xong: {data.ScreenName}", ConsoleColor.Green);
                    }
                    break;

                case "compile":
                    string m = args.Length > 2 ? args[2] : "both";
                    PrintIcon("i", $"Biên dịch {_currentDeviceName} ({m})...", ConsoleColor.Cyan);
                    _tiaEngine.CompileSpecific(_currentDeviceName, m == "hw" || m == "both", m == "sw" || m == "both");
                    PrintIcon("√", "Hoàn tất.", ConsoleColor.Green);
                    break;

                case "run": case "stop": case "download": case "check":
                    HandleOnlineAction(action, args);
                    break;

                default:
                    PrintIcon("×", $"Lệnh 'tia {action}' không xác định.", ConsoleColor.Red);
                    break;
            }
        }

        private static string GetPathOrOpenDialog(string[] args, int index, string filter)
        {
            if (args.Length > index && !string.IsNullOrWhiteSpace(args[index])) return args[index];
            string selectedPath = "";
            Thread t = new Thread(() => {
                using (OpenFileDialog ofd = new OpenFileDialog { Filter = filter, Title = "Chọn file dữ liệu" })
                    if (ofd.ShowDialog(new Form { TopMost = true }) == DialogResult.OK) selectedPath = ofd.FileName;
            });
            t.SetApartmentState(ApartmentState.STA); t.Start(); t.Join();
            return selectedPath;
        }

        private static void HandleChooseDevice(string[] args)
        {
            var devs = _tiaEngine.GetPlcList();
            if (devs == null || devs.Count == 0) { PrintIcon("×", "Project trống.", ConsoleColor.Red); return; }

            if (args.Length > 2 && devs.Any(d => d.Equals(args[2], StringComparison.OrdinalIgnoreCase)))
                _currentDeviceName = devs.First(d => d.Equals(args[2], StringComparison.OrdinalIgnoreCase));
            else {
                Console.WriteLine("\n" + new string('-', 45) + "\n ID | DANH SÁCH PLC TRONG DỰ ÁN\n" + new string('-', 45));
                for (int i = 0; i < devs.Count; i++) Console.WriteLine($" {i + 1,-2} | {devs[i]}");
                Console.Write("\nNhập ID: ");
                if (int.TryParse(Console.ReadLine(), out int idx) && idx > 0 && idx <= devs.Count) _currentDeviceName = devs[idx - 1];
            }
            _currentIp = _tiaEngine.GetDeviceIp(_currentDeviceName);
            PrintIcon("√", $"Đã chọn: {_currentDeviceName} ({_currentIp})", ConsoleColor.Green);
        }

        private static void HandleOnlineAction(string action, string[] args)
        {
            // 1. Lấy danh sách Card mạng
            var adapters = TIA_V20.GetSystemNetworkAdapters();

            if (adapters == null || adapters.Count == 0)
            {
                PrintIcon("×", "Không tìm thấy Card mạng nào.", ConsoleColor.Red);
                return;
            }

            string selectedAdapter = "";

            // 2. Kiểm tra tham số đi kèm (VD: tia download 1)
            if (args.Length > 2)
            {
                string inputArg = args[2];
                if (int.TryParse(inputArg, out int idx) && idx > 0 && idx <= adapters.Count)
                    selectedAdapter = adapters[idx - 1];
                else
                    selectedAdapter = adapters.FirstOrDefault(a => a.Contains(inputArg, StringComparison.OrdinalIgnoreCase));
            }

            // 3. Nếu chưa có card, hiện bảng chọn ID
            if (string.IsNullOrEmpty(selectedAdapter))
            {
                Console.WriteLine("\n" + new string('-', 60));
                Console.ForegroundColor = ConsoleColor.Magenta;
                Console.WriteLine(" ID | NETWORK INTERFACE (PG/PC ADAPTER) ");
                Console.ResetColor();
                Console.WriteLine(new string('-', 60));

                for (int i = 0; i < adapters.Count; i++)
                    Console.WriteLine($" {i + 1,-2} | {adapters[i]}");
                
                Console.WriteLine(new string('-', 60));
                Console.Write("Chọn ID card mạng: ");
                string input = Console.ReadLine();

                if (int.TryParse(input, out int resIdx) && resIdx > 0 && resIdx <= adapters.Count)
                    selectedAdapter = adapters[resIdx - 1];
                else { PrintIcon("!", "Hủy thao tác.", ConsoleColor.Yellow); return; }
            }

            // 4. Hiệu ứng Progress Bar cho lệnh DOWNLOAD
            if (action == "download")
            {
                PrintIcon("i", $"Đang chuẩn bị nạp xuống PLC: {_currentDeviceName}...", ConsoleColor.Cyan);
                Console.Write(" Progress: [");
                for (int i = 0; i <= 20; i++)
                {
                    Console.Write("█");
                    Thread.Sleep(50); // Tạo độ trễ giả lập
                }
                Console.WriteLine("] 100% - OK!");
            }

            // 5. Thực thi lệnh thực tế
            PrintIcon("i", $"Thực thi '{action.ToUpper()}' via {selectedAdapter}...", ConsoleColor.Cyan);
            
            try 
            {
                switch (action)
                {
                    case "run":
                        PrintIcon("√", _tiaEngine.ChangePlcState(_currentDeviceName, _currentIp, selectedAdapter, true), ConsoleColor.Green);
                        break;
                    case "stop":
                        PrintIcon("√", _tiaEngine.ChangePlcState(_currentDeviceName, _currentIp, selectedAdapter, false), ConsoleColor.Green);
                        break;
                    case "download":
                        string res = _tiaEngine.DownloadToPLC(_currentDeviceName, _currentIp, selectedAdapter);
                        Console.WriteLine(res);                       
                        break;
                    case "check":
                        PrintIcon("√", $"Trạng thái Online: {_tiaEngine.GetPlcStatus(_currentDeviceName, selectedAdapter)}", ConsoleColor.Green);
                        break;
                }
            }
            catch (Exception ex) { PrintIcon("×", $"Lỗi: {ex.Message}", ConsoleColor.Red); }
        }

        public static void TiaImportLogic(string blockType, string explicitPath)
        {
            PrintIcon("i", $"--- IMPORT {blockType} ---", ConsoleColor.Cyan);
            string path = explicitPath;
            if (string.IsNullOrEmpty(path)) {
                var latestSclFile = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory).GetFiles("*.scl").OrderByDescending(f => f.LastWriteTime).FirstOrDefault();
                if (latestSclFile != null) path = latestSclFile.FullName;
            }
            if (File.Exists(path)) {
                try {
                    string target = !string.IsNullOrEmpty(_currentDeviceName) && _currentDeviceName != "None" ? _currentDeviceName : _tiaEngine.GetPlcList().FirstOrDefault();
                    _tiaEngine.CreateFBblockFromSource(target, path);
                    PrintIcon("√", $"Nạp thành công vào {target}!", ConsoleColor.Green);
                } catch (Exception ex) { PrintIcon("×", $"Lỗi: {ex.Message}", ConsoleColor.Red); }
            } else PrintIcon("×", "Không tìm thấy file SCL.", ConsoleColor.Red);
        }

        static string ReadLineWithEscape()
        {
            StringBuilder sb = new StringBuilder();
            while (true) {
                var k = Console.ReadKey(true);
                if (k.Key == ConsoleKey.Escape) return null;
                if (k.Key == ConsoleKey.Enter) { Console.WriteLine(); return sb.ToString(); }
                if (k.Key == ConsoleKey.Backspace && sb.Length > 0) { sb.Length--; Console.Write("\b \b"); }
                else if (!char.IsControl(k.KeyChar)) { sb.Append(k.KeyChar); Console.Write(k.KeyChar); }
            }
        }

        public static void PrintIcon(string icon, string msg, ConsoleColor c) { Console.ForegroundColor = c; Console.Write($"[{icon}] "); Console.ResetColor(); Console.WriteLine(msg); }

        static void PrintHelp()
        {
            Console.WriteLine("\n" + new string('=', 85));
            Console.ForegroundColor = ConsoleColor.Magenta;
            Console.WriteLine("                HƯỚNG DẪN CHI TIẾT CÚ PHÁP TIA COPILOT CLI");
            Console.ResetColor();
            Console.WriteLine(new string('=', 85));

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("\n[1-5: QUẢN LÝ DỰ ÁN & KẾT NỐI]");
            Console.ResetColor();
            Console.WriteLine("  tia connect                 : Kết nối TIA Portal đang chạy.");
            Console.WriteLine("  tia open <Path>             : Mở project. VD: tia open \"C:\\Project.ap19\"");
            Console.WriteLine("  tia create <Dir> <Name>     : Tạo project. VD: tia create \"D:\\Project\" \"Station_1\"");
            Console.WriteLine("  tia save                    : Lưu dự án hiện tại.");
            Console.WriteLine("  tia close                   : Đóng dự án và giải phóng tài nguyên.");

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("\n[6-8: THIẾT BỊ & CẤU HÌNH]");
            Console.ResetColor();
            Console.WriteLine("  tia device <Name> <IP> <Type> : Tạo PLC. VD: tia device \"PLC_01\" \"192.168.0.1\" \"S7-1500\"");
            Console.WriteLine("  tia choose <Name>           : Khóa mục tiêu vào PLC. VD: tia choose \"PLC_01\"");
            Console.WriteLine("  tia hmi-conn <H_IP> <P_IP>  : Kết nối HMI-PLC. VD: tia hmi-conn \"192.168.0.2\" \"192.168.0.1\"");

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("\n[9-13: LẬP TRÌNH & DỮ LIỆU]");
            Console.ResetColor();
            Console.WriteLine("  tia fb/fc/ob [Path]         : Import SCL (Mặc định lấy file AI mới nhất).");
            Console.WriteLine("  tia tag-plc <Path>          : Nạp PLC Tags từ CSV. VD: tia tag-plc \"tags.csv\"");
            Console.WriteLine("  tia tag-hmi <Path>          : Nạp HMI Tags từ CSV. VD: tia tag-hmi \"hmi_tags.csv\"");

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("\n[14-16: WINCC UNIFIED & SCADA]");
            Console.ResetColor();
            Console.WriteLine("  tia draw <Path>             : Vẽ màn hình từ JSON. VD: tia draw \"screen.json\"");
            Console.WriteLine("  tia img <Path/Folder>       : Import ảnh đơn hoặc thư mục. VD: tia img \"D:\\Assets\"");
            Console.WriteLine("  tia export <ScreenName>     : Xuất Symbol Path. VD: tia export \"MainScreen\"");

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("\n[17-21: VẬN HÀNH & ONLINE]");
            Console.ResetColor();
            Console.WriteLine("  tia compile [hw/sw/both]    : Biên dịch dự án. VD: tia compile sw");
            Console.WriteLine("  tia download [CardID/Name]  : Đổ code xuống PLC.");
            Console.WriteLine("  tia run [CardID/Name]       : Chuyển PLC sang RUN.");
            Console.WriteLine("  tia stop [CardID/Name]      : Chuyển PLC sang STOP.");
            Console.WriteLine("  tia check [CardID/Name]     : Kiểm tra trạng thái Online của PLC.");

            Console.WriteLine("\n" + new string('-', 85));
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine(" LƯU Ý: Các đường dẫn chứa khoảng trắng bắt buộc bao quanh bằng dấu ngoặc kép \" \".");
            Console.WriteLine(" Gõ 'exit' hoặc bấm [ESC] để kết thúc phiên làm việc.");
            Console.ResetColor();
            Console.WriteLine(new string('=', 85) + "\n");
        }

        private static string SelectAdapter(string inputArg = "")
        {
            var ads = TIA_V20.GetSystemNetworkAdapters();
            if (ads == null || ads.Count == 0) return null;
            if (int.TryParse(inputArg, out int idx) && idx > 0 && idx <= ads.Count) return ads[idx - 1];
            var match = ads.FirstOrDefault(a => a.Contains(inputArg, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrEmpty(match)) return match;

            Console.WriteLine("\n" + new string('-', 45) + "\n ID | NETWORK INTERFACE (PG/PC)\n" + new string('-', 45));
            for (int i = 0; i < ads.Count; i++) Console.WriteLine($" {i + 1,-2} | {ads[i]}");
            Console.Write("\nChọn ID card: ");
            return int.TryParse(Console.ReadLine(), out int result) && result <= ads.Count ? ads[result - 1] : null;
        }
    }
}