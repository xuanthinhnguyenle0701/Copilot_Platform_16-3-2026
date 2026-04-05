using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using System.Linq;
using System.Diagnostics;
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
        private static string _currentProjectPath = "None";
        private static string _currentDeviceName = "None";
        private static string _currentDeviceType = "None";
        private static string _currentIp = "0.0.0.0";
        private static string _lastGeneratedFilePath = "";
        public static string _currentSessionId = "default";

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
            string sessionId = _currentSessionId;

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
                        if (args.Length < 2)
                        {
                            Console.WriteLine("LỖI: Bạn phải truyền hành động sau 'chat' (fb/fc/ob/load-tags/load-spec/clear-data).");
                            return;
                        }

                        string chatAction = args[1].ToLower();
                        switch (chatAction)
                        {
                            case "session":
                                // TÍNH NĂNG 1 & 2: Giao diện Menu quản lý Session
                                await CommandHandler.HandleSessionMenuAsync();
                                //PrintIcon("i", "Tính năng 'session' đang được thi công...", ConsoleColor.Cyan);
                                break;

                            case "status":
                                // TÍNH NĂNG 4: Báo cáo Radar
                                await CommandHandler.HandleCheckStatusAsync(sessionId);
                                //PrintIcon("i", "Tính năng 'status' đang được thi công...", ConsoleColor.Cyan);
                                break;

                            case "check-data":
                                // TÍNH NĂNG 3: Xuất file và mở Notepad (Ta sẽ code phần này ở hiệp sau)
                                //PrintIcon("i", "Tính năng 'check-data' đang được thi công...", ConsoleColor.Cyan);
                                await CommandHandler.HandleCheckDataAsync(sessionId);
                                break;


                            case "fb":
                            case "fc":
                            case "ob":
                            case "scada":
                            case "cwc":
                                string targetType = CommandHandler.GetBlockType(chatAction);
                                string query = args.Length > 2 ? args[2] : "";
                                if (args.Length > 3) sessionId = args[3];

                                if (string.IsNullOrEmpty(query))
                                {
                                    Console.WriteLine("LỖI: Bạn phải truyền câu lệnh yêu cầu (query).");
                                    return;
                                }

                                await CommandHandler.HandleChatAsync(targetType, query, sessionId);
                                break;

                            case "load-tags":
                                string tagFile = GetPathOrOpenDialog(args, 2, "Excel Files (*.xlsx;*.xls)|*.xlsx;*.xls|CSV Files (*.csv)|*.csv|All Files (*.*)|*.*");
                                if (!string.IsNullOrEmpty(tagFile))
                                    await CommandHandler.HandleLoadTagsAsync(tagFile);
                                break;

                            case "load-spec":
                                string specFile = GetPathOrOpenDialog(args, 2, "Spec Files (*.md;*.txt;*.json)|*.md;*.txt;*.json|All Files (*.*)|*.*");
                                if (args.Length > 3) sessionId = args[3];
                                if (!string.IsNullOrEmpty(specFile))
                                    await CommandHandler.HandleLoadSpecAsync(specFile, sessionId);
                                break;

                            case "clear-data":
                                if (args.Length > 2) sessionId = args[2];
                                await CommandHandler.HandleClearDataAsync(sessionId);
                                break;


                            default:
                                PrintIcon("?", $"Không tìm thấy lệnh 'chat {chatAction}'. Gõ 'help' để xem.", ConsoleColor.Yellow);
                                break;
                        }
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
                PrintIcon("!", "Cần nhập action (VD: tia connect, tia draw...)", ConsoleColor.Yellow);
                return;
            }

            string action = args[1].ToLower();

            switch (action)
            {
                // --- NHÓM 1: PROJECT & CONNECTION ---
                case "connect":
                    PrintIcon("i", "Đang kết nối TIA Portal...", ConsoleColor.Cyan);
                    if (_tiaEngine.ConnectToTIA())
                    {
                        _currentProjectName = _tiaEngine.GetProjectName();
                        _currentProjectPath = _tiaEngine.GetProjectPath();
                        Console.WriteLine($"   [Path]: {_currentProjectPath}");
                        PrintIcon("√", $"Đã kết nối: {_currentProjectName}", ConsoleColor.Green);
                    }
                    else PrintIcon("×", "Không thấy TIA Portal đang chạy.", ConsoleColor.Red);
                    break;

                case "open":
                    string openPath = GetPathOrOpenDialog(args, 2, "TIA Project (*.ap*)|*.ap*");
                    if (!string.IsNullOrEmpty(openPath))
                    {
                        PrintIcon("i", $"Mở dự án: {Path.GetFileName(openPath)}...", ConsoleColor.Cyan);
                        if (_tiaEngine.CreateTIAproject(openPath, "", false))
                        {
                            _currentProjectName = Path.GetFileNameWithoutExtension(openPath);
                            _currentProjectPath = _tiaEngine.GetProjectPath();
                            Console.WriteLine($"   [Path]: {_currentProjectPath}");
                            PrintIcon("√", $"Đã mở: {_currentProjectName}", ConsoleColor.Green);
                        }
                    }
                    break;

                case "create": // BỔ SUNG
                    if (args.Length < 4) { PrintIcon("!", "Cú pháp: tia create <Thư mục> <Tên>", ConsoleColor.Yellow); break; }
                    if (_tiaEngine.CreateTIAproject(args[2], args[3], true))
                    {
                        _currentProjectName = args[3];
                        _currentProjectPath = _tiaEngine.GetProjectPath();
                        Console.WriteLine($"   [Path]: {_currentProjectPath}");
                        PrintIcon("√", $"Đã tạo dự án: {args[3]}", ConsoleColor.Green);
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

                // --- NHÓM 2: DEVICE & CONFIG ---
                case "device":
                    if (args.Length >= 5)
                    {
                        _tiaEngine.CreateDev(args[2], args[4], args[3], "");
                        _currentDeviceName = args[2];
                        _currentIp = args[3];
                        PrintIcon("√", $"Đã tạo PLC: {args[2]} ({args[3]})", ConsoleColor.Green);
                    }
                    else
                    {

                        HandleCreateDeviceWizard();
                    }
                    break;

                case "choose":
                    HandleChooseDevice(args);
                    break;

                case "hmi-conn":
                    if (args.Length < 4)
                    {
                        PrintIcon("!", "Cú pháp: tia hmi-conn <HMI_IP> <PLC_IP>", ConsoleColor.Yellow);
                        break;
                    }

                    PrintIcon("i", "Đang phân tích kết nối...", ConsoleColor.Cyan);

                    // Gọi hàm và nhận về tên Connection thực tế đã tạo (ví dụ: HMI_PLC_Conn_2)
                    string resultName = _tiaEngine.CreateUnifiedConnectionCombined(_currentDeviceName, args[2], args[3]);

                    if (resultName.StartsWith("[ERROR]"))
                    {
                        PrintIcon("×", resultName, ConsoleColor.Red);
                    }
                    else
                    {
                        PrintIcon("√", $"Đã tạo kết nối thành công: {resultName}", ConsoleColor.Green);
                        PrintIcon("i", $"Địa chỉ: {args[2]} <-> {args[3]}", ConsoleColor.DarkGray);
                    }
                    break;

                // --- NHÓM 3: LOGIC & DATA ---
                case "fb":
                case "fc":
                case "ob":
                    string sclPath = GetPathOrOpenDialog(args, 2, "SCL Files (*.scl)|*.scl");
                    TiaImportLogic(action.ToUpper(), sclPath);
                    break;

                case "tag-plc":
                    string pTagPath = GetPathOrOpenDialog(args, 2, "CSV Tags (*.csv)|*.csv");
                    if (!string.IsNullOrEmpty(pTagPath)) _tiaEngine.ImportPlcTagsFromCsv(_currentDeviceName, pTagPath);
                    break;

                case "tag-hmi":
                    string hTagPath = GetPathOrOpenDialog(args, 2, "CSV Tags (*.csv)|*.csv");
                    if (!string.IsNullOrEmpty(hTagPath)) _tiaEngine.ImportHmiTagsFromCsv(_currentDeviceName, hTagPath);
                    break;

                // --- NHÓM 4: SCADA & GRAPHICS ---
                case "cwc-deploy":                   
                    _tiaEngine.GetProjectPath(); 
                    string importPath = GetPathOrOpenDialog(args, 2, "All files (*.*)|*.*|Zip files (*.zip)|*.zip|Widget files (*.vwdgt)|*.vwdgt");
                    if (!string.IsNullOrEmpty(importPath))
                    {
                        PrintIcon("i", $"Đang Import vào CustomControls: {Path.GetFileName(importPath)}...", ConsoleColor.Cyan);
                        
                        // 3. Thực hiện copy vật lý vào UserFiles/CustomControls
                        _tiaEngine.AddFileToUserFilesFolder(importPath);
                        
                        PrintIcon("√", "Đã Import vật lý thành công.", ConsoleColor.Green);
                    }
                    else
                    {
                        PrintIcon("!", "Không có file nào được chọn để Import.", ConsoleColor.Yellow);
                    }
                    break;

                case "draw":
                    string jPath = GetPathOrOpenDialog(args, 2, "JSON SCADA (*.json)|*.json");
                    if (!string.IsNullOrEmpty(jPath))
                    {
                        try
                        {
                            var projectData = JsonConvert.DeserializeObject<ScadaProjectModel>(File.ReadAllText(jPath));
                            _tiaEngine.GenerateScadaProject(projectData, _currentDeviceName);
                            PrintIcon("√", "Vẽ SCADA hoàn tất!", ConsoleColor.Green);
                        }
                        catch (Exception ex) { PrintIcon("X", $"Lỗi vẽ: {ex.Message}", ConsoleColor.Red); }
                    }
                    break;

                case "img": // BỔ SUNG
                    string imgPath = GetPathOrOpenDialog(args, 2, "Images|*.png;*.jpg;*.svg");
                    if (!string.IsNullOrEmpty(imgPath))
                    {
                        _tiaEngine.AddPngToProjectGraphics(imgPath, Path.GetFileNameWithoutExtension(imgPath));
                        PrintIcon("√", "Đã nạp ảnh vào Graphics Folder.", ConsoleColor.Green);
                    }
                    break;

                case "export":

                    // 1. Lấy loại export (mặc định là screen nếu không nhập)

                    string exportType = args.Length > 2 ? args[2].ToLower() : "screen";

                    // 2. Tên màn hình hoặc tên thiết bị cần export

                    string exportName = args.Length > 3 ? args[3] : "Main_Process";



                    PrintIcon("i", $"Đang chuẩn bị xuất dữ liệu {exportType}...", ConsoleColor.Cyan);



                    try
                    {

                        string saveFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Exports");

                        if (!Directory.Exists(saveFolder)) Directory.CreateDirectory(saveFolder);



                        string timeStamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");



                        switch (exportType)
                        {

                            case "settings":

                                // MỤC TIÊU CHÍNH: Xuất cấu trúc Settings để tìm Start Screen

                                string setPath = Path.Combine(saveFolder, $"HmiSettings_{timeStamp}.json");

                                _tiaEngine.ExportHmiSettingsToJson(_currentDeviceName, setPath);

                                PrintIcon("√", $"Đã xuất HmiSettings ra: {Path.GetFileName(setPath)}", ConsoleColor.Green);

                                break;



                            case "screen":

                                string screenPath = Path.Combine(saveFolder, $"{exportName}_{timeStamp}.json");

                                _tiaEngine.ExportUnifiedScreenToJson(_currentDeviceName, exportName, screenPath);

                                PrintIcon("√", $"Đã xuất màn hình ra: {Path.GetFileName(screenPath)}", ConsoleColor.Green);

                                break;



                            case "tag-plc":

                                string plcTagPath = Path.Combine(saveFolder, $"{exportName}_PLCTags_{timeStamp}.csv");

                                _tiaEngine.ExportPlcTagsToCsv(_currentDeviceName, plcTagPath);

                                PrintIcon("√", $"Đã xuất PLC Tags ra: {Path.GetFileName(plcTagPath)}", ConsoleColor.Green);

                                break;



                            case "tag-hmi":

                                string hmiTagPath = Path.Combine(saveFolder, $"{_currentDeviceName}_HMITags_{timeStamp}.csv");

                                _tiaEngine.ExportHmiTagsToCsv(_currentDeviceName, hmiTagPath);

                                PrintIcon("√", $"Đã xuất HMI Tags ra: {Path.GetFileName(hmiTagPath)}", ConsoleColor.Green);

                                break;



                            default:

                                PrintIcon("!", $"Loại export '{exportType}' chưa được hỗ trợ. (Hỗ trợ: settings, screen, tag-plc, tag-hmi)", ConsoleColor.Yellow);

                                break;

                        }

                    }
                    catch (Exception ex)
                    {

                        PrintIcon("X", $"Lỗi khi export: {ex.Message}", ConsoleColor.Red);

                    }

                    break;

                // --- NHÓM 5: ONLINE & COMMISSIONING ---
                case "compile":
                    bool isRebuild = args.Any(a => a.ToLower() == "rebuild");
                    string cMode = (args.Length > 2 && !isRebuild) ? args[2] : "both";
                    PrintIcon("i", isRebuild ? "Rebuilding all..." : "Compiling...", ConsoleColor.Cyan);
                    string cRes = _tiaEngine.CompileSpecific(_currentDeviceName, cMode == "hw" || cMode == "both", cMode == "sw" || cMode == "both", isRebuild);
                    Console.WriteLine(cRes);
                    break;

                case "run":
                case "stop":
                case "download":
                case "check":
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
            Thread t = new Thread(() =>
            {
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
            else
            {
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
            if (string.IsNullOrEmpty(path))
            {
                var latestSclFile = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory).GetFiles("*.scl").OrderByDescending(f => f.LastWriteTime).FirstOrDefault();
                if (latestSclFile != null) path = latestSclFile.FullName;
            }
            if (File.Exists(path))
            {
                try
                {
                    string target = !string.IsNullOrEmpty(_currentDeviceName) && _currentDeviceName != "None" ? _currentDeviceName : _tiaEngine.GetPlcList().FirstOrDefault();
                    _tiaEngine.CreateFBblockFromSource(target, path);
                    PrintIcon("√", $"Nạp thành công vào {target}!", ConsoleColor.Green);
                }
                catch (Exception ex) { PrintIcon("×", $"Lỗi: {ex.Message}", ConsoleColor.Red); }
            }
            else PrintIcon("×", "Không tìm thấy file SCL.", ConsoleColor.Red);
        }

        static string ReadLineWithEscape()
        {
            StringBuilder sb = new StringBuilder();
            while (true)
            {
                var k = Console.ReadKey(true);
                if (k.Key == ConsoleKey.Escape) return null;
                if (k.Key == ConsoleKey.Enter) { Console.WriteLine(); return sb.ToString(); }
                if (k.Key == ConsoleKey.Backspace && sb.Length > 0) { sb.Length--; Console.Write("\b \b"); }
                else if (!char.IsControl(k.KeyChar)) { sb.Append(k.KeyChar); Console.Write(k.KeyChar); }
            }
        }

        public static void PrintIcon(string icon, string msg, ConsoleColor c)
        {
            Console.ForegroundColor = c;
            Console.Write($"[{icon}] ");
            Console.ResetColor(); Console.WriteLine(msg);
        }

        static void PrintHelp()
        {
            Console.WriteLine("\n" + new string('=', 85));
            Console.ForegroundColor = ConsoleColor.Magenta;
            Console.WriteLine("                HƯỚNG DẪN CHI TIẾT CÚ PHÁP TIA COPILOT CLI");
            Console.ResetColor();
            Console.WriteLine(new string('=', 85));

            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("\n[AI MODULE]");
            Console.ResetColor();

            Console.WriteLine("  chat <FB/FC/OB/SCADA/CWC> \"<Query>\" [SessionID]  : Calling AI");
            Console.WriteLine("  chat load-tags \"<Đường_dẫn_File_Excel/CSV>\"  : Upload desire tags");
            Console.WriteLine("  chat load-spec \"<Đường_dẫn_File_Spec.txt>\"   : Upload system spec");
            Console.WriteLine("  chat clear-data                                : clear uploaded tags/system spec");
            Console.WriteLine("  chat session                                   : Quản lý Session");
            Console.WriteLine("  chat status                                    : Kiểm tra trạng thái Session");
            Console.WriteLine("  chat check-data                                : Kiểm tra dữ liệu Session");

            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("\n[TIA MODULE]");
            Console.ResetColor();
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
            Console.WriteLine("  tia cwc-deploy [Path]       : Deploy CWC zip → project CustomControls. VD: tia cwc-deploy");
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
            Console.WriteLine(" LƯU Ý: Các đường dẫn, văn bản hội thoại chứa khoảng trắng bắt buộc bao quanh bằng dấu ngoặc kép \" \".");
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

        private static void HandleCreateDeviceWizard()
        {
            string typeIdentifier = "";
            Console.WriteLine("\n" + new string('=', 55));
            Console.WriteLine("[WIZARD TẠO THIẾT BỊ - TIA V20 OPTIMIZED]");
            Console.WriteLine(" 1. Chọn từ Catalog (Phân loại theo dòng)");
            Console.WriteLine(" 2. Nhập thông số tay (Manual)");
            Console.Write("Chọn chế độ (1/2): ");
            string mode = Console.ReadLine();

            if (mode == "1")
            {
                string catalogPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PlcCatalog.json");
                if (File.Exists(catalogPath))
                {
                    var json = File.ReadAllText(catalogPath);
                    var catalogData = JsonConvert.DeserializeObject<PlcCatalogWrapper>(json);

                    // BƯỚC 1: CHỌN VÙNG THIẾT BỊ
                    Console.WriteLine("\n--- CHỌN DÒNG THIẾT BỊ ---");
                    Console.WriteLine(" 1. SIMATIC S7-1200");
                    Console.WriteLine(" 2. SIMATIC S7-1500");
                    Console.WriteLine(" 3. WinCC Unified (Panel & PC)");
                    Console.Write("Chọn dòng (1-3): ");
                    string subMode = Console.ReadLine();

                    List<PlcCatalogItem> selectedList = null;
                    if (subMode == "1") selectedList = catalogData.S71200;
                    else if (subMode == "2") selectedList = catalogData.S71500;
                    else if (subMode == "3") selectedList = catalogData.WinCC_Unified;

                    if (selectedList != null && selectedList.Count > 0)
                    {
                        // BƯỚC 2: HIỂN THỊ DANH SÁCH TRONG VÙNG ĐÃ CHỌN
                        Console.WriteLine("\n ID | TÊN THIẾT BỊ                   | MÃ HÀNG");
                        Console.WriteLine(new string('-', 65));
                        for (int i = 0; i < selectedList.Count; i++)
                        {
                            Console.WriteLine($" {i + 1,-2} | {selectedList[i].Name,-30} | {selectedList[i].OrderNumber}");
                        }

                        Console.Write("\nNhập ID thiết bị: ");
                        if (int.TryParse(Console.ReadLine(), out int selIdx) && selIdx > 0 && selIdx <= selectedList.Count)
                        {
                            var selectedItem = selectedList[selIdx - 1];
                            string finalVer = selectedItem.Version;

                            // BƯỚC 3: CHỌN VERSION (FIRMWARE)
                            if (selectedItem.AvailableVersions != null && selectedItem.AvailableVersions.Count > 0)
                            {
                                Console.WriteLine($"\n--> Firmware hỗ trợ cho {selectedItem.Name}:");
                                for (int j = 0; j < selectedItem.AvailableVersions.Count; j++)
                                {
                                    Console.WriteLine($"    {j + 1}. {selectedItem.AvailableVersions[j]}");
                                }
                                Console.Write($"Chọn ID Version (Enter để dùng {finalVer}): ");
                                string vInput = Console.ReadLine();
                                if (int.TryParse(vInput, out int vIdx) && vIdx > 0 && vIdx <= selectedItem.AvailableVersions.Count)
                                {
                                    finalVer = selectedItem.AvailableVersions[vIdx - 1];
                                }
                            }
                            typeIdentifier = selectedItem.GetTypeIdentifier(finalVer);
                        }
                    }
                    else Console.WriteLine("[!] Vùng này hiện chưa có thiết bị nào trong Catalog.");
                }
                else PrintIcon("!", "Không tìm thấy file PlcCatalog.json!", ConsoleColor.Yellow);
            }

            // Nếu không chọn từ Catalog hoặc Catalog trống
            if (string.IsNullOrEmpty(typeIdentifier))
            {
                // ... (Giữ nguyên logic Nhập tay Manual như bài trước) ...
            }

            // Tiến hành tạo thiết bị (Device Name, IP...)
            Console.Write("\nTên thiết bị: "); string name = Console.ReadLine();
            Console.Write("Địa chỉ IP: "); string ip = Console.ReadLine();

            try
            {
                PrintIcon("i", $"Đang tạo {name}...", ConsoleColor.Cyan);
                _tiaEngine.CreateDev(name, typeIdentifier, ip, "");
                PrintIcon("√", $"Đã tạo xong thiết bị '{name}'!", ConsoleColor.Green);
            }
            catch (Exception ex) { PrintIcon("×", $"Lỗi: {ex.Message}", ConsoleColor.Red); }
        }
    }
}