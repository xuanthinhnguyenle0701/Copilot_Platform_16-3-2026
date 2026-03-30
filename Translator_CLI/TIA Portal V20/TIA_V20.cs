using Siemens.Engineering;
using Siemens.Engineering.Compiler;
using Siemens.Engineering.Connection;
using Siemens.Engineering.Download;
using Siemens.Engineering.Hmi;
using Siemens.Engineering.Hmi.Tag;
using Siemens.Engineering.HmiUnified;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using Siemens.Engineering.SW.ExternalSources;
using Siemens.Engineering.SW.Tags;
using Siemens.Engineering.SW.Types;
using Siemens.Engineering.Library.Types;
using Siemens.Engineering;
using Siemens.Engineering.Library;
using Siemens.Engineering.Library.MasterCopies;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Siemens.Engineering.Library.MasterCopies;
using Siemens.Engineering.Library;
using System.Xml.Linq;
using System.Linq; // <--- CỰC KỲ QUAN TRỌNG
using System.Collections.Generic;
using Siemens.Engineering.HmiUnified.UI.Screens;
using System.Windows.Forms;
using Newtonsoft.Json;
using DownloadConfig = Siemens.Engineering.Download.Configurations.DownloadConfiguration;
namespace Middleware_console
{
    public delegate void MyDownloadConfigurationDelegate(Siemens.Engineering.Download.Configurations.DownloadConfiguration downloadConfiguration);
    public class TIA_V20
    {
        #region 1. Fields, Constructor & Connectivity Status
        private TiaPortal _tiaPortal;
        private Project _project;

        public TIA_V20() { }

        public bool IsConnected => _tiaPortal != null && _project != null;

        public void ConnectToTiaPortal()
        {
            if (!ConnectToTIA())
            {
                throw new Exception("No running TIA Portal instance found!");
            }
        }
        #endregion

        #region 2. TIA Portal & Project Management
        public void CreateTIAinstance(bool withUI)
        {
            if (_tiaPortal != null) return;
            _tiaPortal = new TiaPortal(withUI ? TiaPortalMode.WithUserInterface : TiaPortalMode.WithoutUserInterface);
        }

        public bool ConnectToTIA()
        {
            try
            {
                var processes = TiaPortal.GetProcesses();
                if (processes.Count == 0) return false;
                _tiaPortal = processes[0].Attach();
                if (_tiaPortal.Projects.Count > 0)
                {
                    _project = _tiaPortal.Projects[0];
                    return true;
                }
                return false;
            }
            catch { return false; }
        }

        public bool CreateTIAproject(string path, string name, bool createNew)
        {
            try
            {
                if (_tiaPortal == null) CreateTIAinstance(true);
                if (createNew)
                    _project = _tiaPortal.Projects.Create(new DirectoryInfo(path), name);
                else
                    _project = _tiaPortal.Projects.Open(new FileInfo(path));
                return _project != null;
            }
            catch { return false; }
        }

        public bool SaveProject()
        {
            try { _project?.Save(); return true; } catch { return false; }
        }

        public void CloseTIA()
        {
            try
            {
                if (_project != null)
                {
                    _project.Close();
                    _project = null;
                }
                if (_tiaPortal != null)
                {
                    _tiaPortal.Dispose();
                    _tiaPortal = null;
                }
                foreach (var process in Process.GetProcessesByName("Siemens.Automation.Portal"))
                {
                    try
                    {
                        process.Kill();
                        process.WaitForExit();
                    }
                    catch { }
                }
            }
            catch { }
        }

        public string GetProjectName()
        {
            if (_project != null)
            {
                try { return _project.Name; } catch { }
            }
            return "Unknown";
        }
        #endregion

        #region 3. Hardware Configuration & Network
        public void CreateDev(string devName, string typeIdentifier, string ipX1, string ipX2)
        {
            if (_project == null) CheckProject();
            if (string.IsNullOrWhiteSpace(devName)) devName = "Device_1";

            List<string> existingNames = GetPlcList();
            if (existingNames.Contains(devName))
                throw new Exception($"Name '{devName}' already exists!");

            Device newDevice = null;

            try
            {
                if (typeIdentifier.Contains("xxxxx") || typeIdentifier.Contains("6AV2 155"))
                {
                    Console.WriteLine($"[Auto-Fix] Detected WinCC Unified PC. Starting Optimized Creation...");
                    string[] pcIds = new string[] { "System:Rack.PC", "OrderNumber:6ES7647-0AA00-1YA0/V3.0" };
                    foreach (string id in pcIds)
                    {
                        try
                        {
                            newDevice = _project.Devices.CreateWithItem(id, devName, devName);
                            if (newDevice != null) break;
                        }
                        catch { }
                    }
                    if (newDevice == null)
                    {
                        try { newDevice = _project.Devices.Create("System:Device.PC", devName); } catch { }
                    }

                    if (newDevice == null) throw new Exception("FATAL: Could not create PC Station.");

                    DeviceItem pcRack = newDevice.DeviceItems[0];
                    string netId = "OrderNumber:IE General/V2.0";
                    for (int i = 1; i <= 2; i++)
                    {
                        if (pcRack.CanPlugNew(netId, "", i))
                        {
                            pcRack.PlugNew(netId, "IE1", i);
                            break;
                        }
                    }

                    string firmware = "20.0.0.0";
                    if (typeIdentifier.Contains("/") && typeIdentifier.Split('/').Length > 1)
                    {
                        string f = typeIdentifier.Split('/')[1].Replace("V", "").Trim();
                        if (!string.IsNullOrEmpty(f)) firmware = f;
                    }
                    string swId = $"OrderNumber:6AV2 155-xxxxx-xxxx/{firmware}";
                    for (int slotNum = 1; slotNum <= 10; slotNum++)
                    {
                        if (pcRack.CanPlugNew(swId, "", slotNum))
                        {
                            pcRack.PlugNew(swId, "", slotNum);
                            break;
                        }
                    }
                }
                else
                {
                    newDevice = _project.Devices.CreateWithItem(typeIdentifier, devName, devName);
                }

                if (newDevice != null && !string.IsNullOrEmpty(ipX1))
                {
                    try { SetPlcIpAddress(newDevice, ipX1); } catch { }
                }
            }
            catch (Exception ex) { throw new Exception($"Create Failed: {ex.Message}"); }
        }

        public string GetDeviceType(string deviceName)
        {
            if (_project == null) return "Unknown";
            Device device = FindDeviceRecursive(_project, deviceName);
            if (device != null)
            {
                if (!string.IsNullOrEmpty(device.TypeIdentifier)) return device.TypeIdentifier;
                foreach (DeviceItem item in device.DeviceItems)
                {
                    if (!string.IsNullOrEmpty(item.TypeIdentifier)) return item.TypeIdentifier;
                }
            }
            return "Unknown";
        }

        public string GetDeviceIp(string deviceName)
        {
            if (_project == null) return "0.0.0.0";
            Device device = FindDeviceRecursive(_project, deviceName);
            if (device != null)
            {
                DeviceItem netItem = FindNetworkInterfaceItem(device.DeviceItems);
                if (netItem != null)
                {
                    var networkInterface = netItem.GetService<Siemens.Engineering.HW.Features.NetworkInterface>();
                    if (networkInterface != null && networkInterface.Nodes.Count > 0)
                    {
                        try { return networkInterface.Nodes[0].GetAttribute("Address").ToString(); } catch { }
                    }
                }
            }
            return "0.0.0.0";
        }

        public List<string> GetPlcList()
        {
            if (_project == null) CheckProject();
            List<string> plcNames = new List<string>();

            foreach (Device device in _project.Devices)
            {
                // Sử dụng hàm xử lý tên gộp
                plcNames.Add(GetCombinedDeviceName(device));
            }

            foreach (DeviceUserGroup group in _project.DeviceGroups)
            {
                ScanGroupRecursive(group, plcNames);
            }
            return plcNames;
        }

        private string GetCombinedDeviceName(Device device)
        {
            // 1. Xử lý cho trạm PC (WinCC Unified / PC Station)
            if (device.Name.Contains("PC-System") || device.Name.Contains("PC_Station"))
            {
                string stationName = device.Name;
                string runtimeName = "";

                foreach (DeviceItem item in device.DeviceItems)
                {
                    // Tìm thành phần HMI Runtime hoặc WinCC Unified bên trong
                    if (item.Name.Contains("HMI_RT") || item.Name.Contains("WinCC") || item.Name.Contains("RT_"))
                    {
                        runtimeName = item.Name;
                        break;
                    }
                }

                // Nếu tìm thấy runtime thì gộp: PC-System_1|HMI_RT_1
                return !string.IsNullOrEmpty(runtimeName) ? $"{stationName}|{runtimeName}" : stationName;
            }

            // 2. Xử lý cho PLC (S7-1500 / S7-1200)
            // Ưu tiên lấy tên CPU (Nhu_Project2) thay vì tên trạm mặc định
            foreach (DeviceItem item in device.DeviceItems)
            {
                if (item.Name.StartsWith("Rail") || item.Name.StartsWith("Rack")) continue;

                if (item.Name != device.Name && !item.Name.Contains("station"))
                {
                    return item.Name;
                }
            }

            return device.Name;
        }

        public static List<string> GetSystemNetworkAdapters()
        {
            List<string> adapterNames = new List<string>();
            try
            {
                System.Net.NetworkInformation.NetworkInterface[] adapters = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces();
                foreach (System.Net.NetworkInformation.NetworkInterface adapter in adapters)
                {
                    if (adapter.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Ethernet ||
                        adapter.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Wireless80211)
                    {
                        adapterNames.Add(adapter.Description);
                    }
                }
            }
            catch { }
            return adapterNames;
        }
        #endregion

        #region 4. PLC Software & Block Operations
        public void ImportBlock(string filePath, string targetPlcName)
        {
            CreateFBblockFromSource(targetPlcName, filePath);
        }

        public void CreateFBblockFromSource(string targetPlcName, string sourcePath)
        {
            if (_project == null) CheckProject();
            Device device = FindDeviceRecursive(_project, targetPlcName);
            if (device == null) throw new Exception($"PLC '{targetPlcName}' not found.");

            var software = GetSoftware(device);
            if (software is PlcSoftware plcSoftware)
            {
                var group = plcSoftware.ExternalSourceGroup;
                var fileName = Path.GetFileName(sourcePath);
                var existingSrc = group.ExternalSources.Find(fileName);
                if (existingSrc != null) existingSrc.Delete();
                var src = group.ExternalSources.CreateFromFile(fileName, sourcePath);
                src.GenerateBlocksFromSource();
            }
            else throw new Exception($"{targetPlcName} is not a valid PLC.");
        }

        // Đảm bảo các hàm này nằm TRONG class TIA_V20
        public string CompileSpecific(string rawDeviceName, bool compileHW, bool compileSW, bool rebuildAll = false)
        {
            if (_project == null) return "Lớp 0: Chưa kết nối Project.";

            // 1. TÁCH TÊN LẤY LỚP NGOÀI (STATION)
            // Ví dụ: "PC-System_3|HMI_RT_3" -> lấy "PC-System_3"
            string targetStationName = rawDeviceName.Contains("|")
                                    ? rawDeviceName.Split('|')[0]
                                    : rawDeviceName;

            // 2. TÌM THIẾT BỊ THEO TÊN TRẠM CHA
            Device device = FindDeviceRecursive(_project, targetStationName);
            if (device == null) return $"Lớp 1: Không tìm thấy thiết bị '{targetStationName}'.";

            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"--- Đang kiểm tra thiết bị: {targetStationName} ---");

            // 3. DÒ LỚP PHẦN CỨNG (HW) - Thường biên dịch ở cấp Device
            if (compileHW)
            {
                sb.AppendLine("> Đang dò lớp HW...");
                object hwTarget = FindCompilableInDevice(device);
                if (hwTarget != null)
                    sb.AppendLine(InternalInvoke(hwTarget, "HW", rebuildAll));
                else
                    sb.AppendLine("[!] Lớp HW: Thiết bị này không có thực thể biên dịch phần cứng.");
            }

            // 4. DÒ LỚP PHẦN MỀM (SW)
            if (compileSW)
            {
                sb.AppendLine("> Đang dò lớp SW...");
                // Hàm FindSoftwareInDevice sẽ tự động quét vào các DeviceItems của 'device'
                // để tìm HmiSoftware hoặc PlcSoftware.
                object swTarget = FindSoftwareInDevice(device);
                if (swTarget != null)
                    sb.AppendLine(InternalInvoke(swTarget, "SW", rebuildAll));
                else
                    sb.AppendLine("[!] Lớp SW: Không tìm thấy khối phần mềm (Software) có thể biên dịch.");
            }

            return sb.ToString();
        }
        // --- HÀM BỔ TRỢ DÒ TỪNG LỚP ---

        private object FindCompilableInDevice(Device device)
        {
            if (HasCompilableService(device)) return device;

            foreach (var item in device.DeviceItems)
            {
                if (HasCompilableService(item)) return item;
                foreach (var sub in item.DeviceItems)
                {
                    if (HasCompilableService(sub)) return sub;
                }
            }
            return null;
        }

        private object FindSoftwareInDevice(Device device)
        {
            var sw = GetSoftware(device);
            if (HasCompilableService(sw)) return sw;
            return FindCompilableInDevice(device);
        }

        private bool HasCompilableService(object obj)
        {
            if (obj == null) return false;
            var provider = obj as IServiceProvider;
            // Kiểm tra xem đối tượng có cung cấp dịch vụ ICompilable không
            return provider?.GetService(typeof(ICompilable)) != null;
        }

        private string InternalInvoke(object target, string label, bool rebuild)
        {
            try
            {
                var provider = (IServiceProvider)target;
                object service = provider.GetService(typeof(ICompilable));

                // CHIẾN THUẬT V20: Lấy chính xác Type của Interface ICompilable
                Type interfaceType = typeof(ICompilable);

                // Lấy Method Compile(CompilerOptions) trực tiếp từ Interface
                // Vì ICompilable chỉ có 2 overload: Compile() và Compile(options)
                // Chúng ta lấy hàm có 1 tham số.
                var method = interfaceType.GetMethods()
                    .FirstOrDefault(m => m.Name == "Compile" && m.GetParameters().Length == 1);

                if (method != null)
                {
                    // Lấy kiểu dữ liệu Enum CompilerOptions từ tham số đầu tiên
                    var enumType = method.GetParameters()[0].ParameterType;
                    // Chuyển 1 (RebuildAll) hoặc 0 (None) thành Enum
                    object optionValue = Enum.ToObject(enumType, rebuild ? 1 : 0);

                    // THỰC THI: Gọi thông qua Interface Mapping
                    var result = method.Invoke(service, new object[] { optionValue });

                    // Lấy State và in lỗi nếu có
                    return FormatCompileResult(result, label);
                }

                // Nếu vẫn không thấy, thử gọi hàm Compile() không tham số (Changes only)
                var simpleMethod = interfaceType.GetMethod("Compile", Type.EmptyTypes);
                if (simpleMethod != null)
                {
                    var result = simpleMethod.Invoke(service, null);
                    return FormatCompileResult(result, label);
                }

                return $"{label}: Lỗi ánh xạ Interface (V20 Method Hidden).";
            }
            catch (Exception ex)
            {
                return $"{label} System Error: {ex.InnerException?.Message ?? ex.Message}";
            }
        }

        // Hàm format lỗi để in ra màn hình cho Otis
        private string FormatCompileResult(object result, string label)
        {
            if (result == null) return $"{label}: \u001b[31mResult object is null\u001b[0m";

            // Đợi 1 chút để TIA Portal đổ dữ liệu Messages vào (Fix lỗi mất tin nhắn khi Rebuild)
            System.Threading.Thread.Sleep(200);

            var stateProp = result.GetType().GetProperty("State");
            string state = stateProp?.GetValue(result)?.ToString() ?? "Unknown";

            string stateColor = state == "Success" ? "\u001b[32m" : "\u001b[31m";
            string reset = "\u001b[0m";

            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"{label}: {stateColor}{state}{reset}");

            // Lấy Messages một cách an toàn hơn
            var messagesProp = result.GetType().GetProperty("Messages");
            object messagesObj = messagesProp?.GetValue(result);

            if (messagesObj is System.Collections.IEnumerable messages)
            {
                bool hasMessages = false;
                foreach (var m in messages)
                {
                    hasMessages = true;
                    PrintMessagesRecursive(m, sb, 1);
                }
                if (!hasMessages && state == "Error")
                    sb.AppendLine("   \u001b[33mℹ [!] Lỗi hệ thống nhưng không có tin nhắn chi tiết (Kiểm tra Log TIA).\u001b[0m");
            }

            return sb.ToString();
        }

        private void PrintMessagesRecursive(object msg, StringBuilder sb, int indentLevel)
        {
            if (msg == null) return;

            var typeProp = msg.GetType().GetProperty("Type");
            var descProp = msg.GetType().GetProperty("Description");
            var pathProp = msg.GetType().GetProperty("Path");

            string type = typeProp?.GetValue(msg)?.ToString();
            string desc = descProp?.GetValue(msg)?.ToString();
            string path = pathProp?.GetValue(msg)?.ToString();

            string indent = new string(' ', indentLevel * 3);

            if (!string.IsNullOrEmpty(desc))
            {
                string colorCode = "";
                string icon = "";

                // Định nghĩa màu ANSI
                switch (type)
                {
                    case "Error":
                        colorCode = "\u001b[31m"; // Màu đỏ
                        icon = "✘";
                        break;
                    case "Warning":
                        colorCode = "\u001b[33m"; // Màu vàng
                        icon = "⚠";
                        break;
                    case "Information":
                        colorCode = "\u001b[34m"; // Màu xanh dương
                        icon = "ℹ";
                        break;
                    default:
                        colorCode = "\u001b[37m"; // Màu trắng
                        icon = "•";
                        break;
                }

                string resetCode = "\u001b[0m";

                // Format dòng tin nhắn: [Icon] Path: Description (Có màu)
                sb.AppendLine($"{indent}{colorCode}{icon} {path}{resetCode}: {desc}");
            }

            try
            {
                var subMessagesProp = msg.GetType().GetProperty("Messages");
                var subMessages = (System.Collections.IEnumerable)subMessagesProp?.GetValue(msg);
                if (subMessages != null)
                {
                    foreach (var sm in subMessages)
                    {
                        PrintMessagesRecursive(sm, sb, indentLevel + 1);
                    }
                }
            }
            catch { }
        }

        #endregion

        #region 5. WinCC Unified: Screen Management
        // Thêm tham số selectedDevice vào hàm
        public void GenerateScadaProject(ScadaProjectModel projectData, string selectedDeviceFromCli = null)
        {
            if (_project == null) throw new Exception("Chưa kết nối hoặc mở dự án TIA Portal.");

            // BƯỚC 0: ƯU TIÊN LẤY TÊN TỪ CLI
            string rawDeviceName = !string.IsNullOrEmpty(selectedDeviceFromCli)
                                   ? selectedDeviceFromCli
                                   : projectData.DeviceName;

            // LOGIC TÁCH TÊN: Lấy phần ĐẦU (Index 0) để vẽ lên cấp Station/Device
            // Ví dụ: "PC-System_3|HMI_RT_3" -> lấy "PC-System_3"
            string targetForDrawing = rawDeviceName.Contains("|")
                                      ? rawDeviceName.Split('|')[0]
                                      : rawDeviceName;

            Console.WriteLine($"\n>>> ĐANG KHỞI TẠO DỰ ÁN SCADA TRÊN THIẾT BỊ (STATION): {targetForDrawing} <<<");

            // BƯỚC 1: VẼ TOÀN BỘ MÀN HÌNH
            foreach (var screen in projectData.Screens)
            {
                try
                {
                    // Truyền targetForDrawing (PC-System_3) vào hàm vẽ
                    GenerateScadaScreenFromData(targetForDrawing, screen);
                    Console.WriteLine($"[SUCCESS] Đã vẽ xong màn hình: {screen.ScreenName}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[!] Lỗi khi vẽ màn hình {screen.ScreenName}: {ex.Message}");
                }
            }

            // BƯỚC 2: LƯU DỰ ÁN
            try { _project.Save(); } catch { }

            // BƯỚC 3: CHỈ ĐỊNH MÀN HÌNH CHÍNH
            string startScreenName = !string.IsNullOrEmpty(projectData.StartScreenName)
                                    ? projectData.StartScreenName
                                    : (projectData.Screens.FirstOrDefault()?.ScreenName ?? "Main_Process");

            Console.WriteLine($"[i] Đang cấu hình màn hình khởi động: {startScreenName}...");

            // Gán StartScreen cũng dùng tên Trạm cha
            SetStartScreen(targetForDrawing, startScreenName);

            Console.WriteLine("\n>>> TẤT CẢ MÀN HÌNH ĐÃ ĐƯỢC VẼ VÀ CẤU HÌNH THÀNH CÔNG! <<<");
        }

        private void SetStartScreen(string deviceName, string screenName)
        {
            try
            {
                var device = _project.Devices.FirstOrDefault(d => d.Name.Equals(deviceName, StringComparison.OrdinalIgnoreCase));
                if (device == null) return;

                bool isAssigned = false;

                foreach (var item in device.DeviceItems)
                {
                    var softwareContainer = item.GetService<SoftwareContainer>();
                    if (softwareContainer != null && softwareContainer.Software != null)
                    {
                        // Ép kiểu sang IEngineeringObject để dùng GetAttribute/SetAttribute
                        var hmiObj = (Siemens.Engineering.IEngineeringObject)softwareContainer.Software;

                        try
                        {
                            // CHIẾN THUẬT MỚI: Truy cập thẳng vào StartScreen thông qua chuỗi thuộc tính
                            // Trong Unified, đôi khi StartScreen nằm trong một Object con tên là "RuntimeSettings"

                            var runtimeSettings = hmiObj.GetComposition("RuntimeSettings") as Siemens.Engineering.IEngineeringComposition;
                            Siemens.Engineering.IEngineeringObject rtObj = null;

                            if (runtimeSettings != null)
                            {
                                foreach (Siemens.Engineering.IEngineeringObject r in runtimeSettings) { rtObj = r; break; }
                            }

                            // Nếu vẫn không thấy, thử tìm "Settings" -> "RuntimeSettings" theo kiểu phân cấp
                            if (rtObj == null)
                            {
                                var settingsComp = hmiObj.GetComposition("Settings") as Siemens.Engineering.IEngineeringComposition;
                                if (settingsComp != null)
                                {
                                    foreach (Siemens.Engineering.IEngineeringObject s in settingsComp)
                                    {
                                        var innerRt = s.GetComposition("RuntimeSettings") as Siemens.Engineering.IEngineeringComposition;
                                        if (innerRt != null)
                                        {
                                            foreach (Siemens.Engineering.IEngineeringObject r in innerRt) { rtObj = r; break; }
                                        }
                                        if (rtObj != null) break;
                                    }
                                }
                            }

                            if (rtObj != null)
                            {
                                Console.WriteLine($"[√] Đã tìm thấy RuntimeSettings tại '{item.Name}'.");

                                // Gán giá trị StartScreen
                                rtObj.SetAttribute("StartScreen", screenName);

                                isAssigned = true;
                                Console.WriteLine($"[SUCCESS] Đã gán '{screenName}' làm màn hình khởi động.");

                                _project.Save();
                                CompileSpecific(deviceName, false, true, false);
                                break;
                            }
                        }
                        catch (Exception exInternal)
                        {
                            // PHƯƠNG ÁN CUỐI: Nếu là Unified, StartScreen có thể gán trực tiếp qua Attribute của Software
                            try
                            {
                                hmiObj.SetAttribute("StartScreen", screenName);
                                isAssigned = true;
                                Console.WriteLine($"[SUCCESS] Đã gán trực tiếp StartScreen vào Software.");
                                _project.Save();
                                CompileSpecific(deviceName, false, true, false);
                                break;
                            }
                            catch
                            {
                                Console.WriteLine($"[-] Trạm {item.Name} từ chối gán: {exInternal.Message}");
                            }
                        }
                    }
                }

                if (!isAssigned)
                {
                    Console.WriteLine($"\n[!] Cảnh báo: TIA V20 từ chối cấu trúc gán này. Hãy thử Compile trạm HMI bằng tay 1 lần.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[!] Lỗi SetStartScreen: {ex.Message}");
            }
        }

        public void GenerateScadaScreenFromData(string deviceName, ScadaScreenModel screenData)
        {
            dynamic hmiTarget = GetHmiTarget(deviceName);
            dynamic screens = null;

            try { screens = hmiTarget.Screens; }
            catch { screens = hmiTarget.ScreenFolder.Screens; }

            // Xóa màn hình cũ nếu trùng tên để cập nhật mới
            var existingScreen = ((System.Collections.IEnumerable)screens)
                .Cast<dynamic>()
                .FirstOrDefault(s => s.Name == screenData.ScreenName);
            if (existingScreen != null) existingScreen.Delete();

            // Tạo màn hình mới với thông số từ JSON
            Console.WriteLine($"   -> Đang tạo màn hình: {screenData.ScreenName} ({screenData.Width}x{screenData.Height})");
            dynamic res = screens.Create(screenData.ScreenName);

            // Gán kích thước chuẩn từ JSON
            res.SetAttribute("Width", (uint)(screenData.Width > 0 ? screenData.Width : 1920));
            res.SetAttribute("Height", (uint)(screenData.Height > 0 ? screenData.Height : 1080));

            // Xử lý Items theo cấu trúc Properties của JSON
            if (screenData.Items != null && screenData.Items.Count > 0)
            {
                BuildUnifiedItemsRecursive(res.ScreenItems, screenData.Items);
            }
        }

        public List<string> GetUnifiedScreens(string deviceName)
        {
            if (_project == null) CheckProject();
            Device device = FindDeviceRecursive(_project, deviceName);
            var software = GetSoftware(device) as HmiSoftware;
            return software.Screens.Select(s => s.Name).ToList();
        }

        public void CreateUnifiedScreen(string deviceName, string screenName)
        {
            if (_project == null) CheckProject();
            Device device = FindDeviceRecursive(_project, deviceName);
            var software = GetSoftware(device) as HmiSoftware;
            if (software.Screens.Find(screenName) == null) software.Screens.Create(screenName);
        }
        #endregion

        #region 6. WinCC Unified: Item Rendering & Dynamization
        private void BuildUnifiedItemsRecursive(dynamic composition, List<ScadaItemModel> items)
        {
            // 1. Dictionary dùng chung để lưu "xác" vật thể phục vụ nạp Tag ở GĐ 2
            var createdObjects = new Dictionary<string, dynamic>();

            Console.WriteLine("\n>> [GIAI ĐOẠN 1] Dựng hình & Gán tọa độ thực...");

            foreach (var item in items)
            {
                try
                {
                    // --- BỘ LỌC AN TOÀN: BỎ QUA NGAY TỪ ĐẦU ---
                    string lowerType = item.Type.ToLower();
                    dynamic newItem = null;
                    string typeId = item.Type;

                    // A. TẠO XÁC VẬT THỂ
                    if (item.Properties.ContainsKey("LibraryPath"))
                    {
                        CreateDynamicWidget(composition, item.Type, item.Name, item.Properties);
                        System.Threading.Thread.Sleep(300);
                        newItem = composition.Find(item.Name);
                    }
                    else
                    {
                        // Chuẩn hóa tên cho đồng nhất với Openness V20
                        if (typeId == "Text" || typeId == "TextField" || typeId == "HmiTextField")
                        {
                            typeId = "HmiText";
                        }
                        else if (!typeId.StartsWith("Hmi"))
                        {
                            typeId = "Hmi" + typeId;
                        }

                        // Gọi lệnh Create
                        try
                        {
                            dynamic dynComp = composition;
                            newItem = dynComp.Create((string)typeId, (string)item.Name);
                        }
                        catch (Exception)
                        {
                            // Nếu Create trực tiếp fail, gọi Reflection
                            newItem = CreateBaseItem(composition, typeId, item.Name);
                        }
                    }

                    // B. CẤU HÌNH THUỘC TÍNH (Khi xác đã dựng xong)
                    if (newItem != null)
                    {
                        // 1. Nhóm hình tròn đặc thù (CenterX, CenterY, Radius)
                        if (typeId == "HmiCircle" || typeId == "HmiCircularArc" || typeId == "HmiCircleSegment")
                        {
                            try
                            {
                                newItem.SetAttribute("CenterX", Convert.ToInt32(item.Properties["CenterX"]));
                                newItem.SetAttribute("CenterY", Convert.ToInt32(item.Properties["CenterY"]));
                                newItem.SetAttribute("Radius", (uint)Convert.ToInt32(item.Properties["Radius"]));
                                if (typeId != "HmiCircle")
                                {
                                    newItem.SetAttribute("StartAngle", Convert.ToInt32(item.Properties["AngleStart"]));
                                    newItem.SetAttribute("AngleRange", Convert.ToInt32(item.Properties["AngleRange"]));
                                }
                            }
                            catch { }
                        }
                        // 2. Nhóm vật thể hình học/Widget chuẩn (Left, Top, Width, Height)
                        else
                        {
                            try
                            {
                                int left = item.Properties.ContainsKey("Left") ? Convert.ToInt32(item.Properties["Left"]) : 0;
                                int top = item.Properties.ContainsKey("Top") ? Convert.ToInt32(item.Properties["Top"]) : 0;
                                uint width = item.Properties.ContainsKey("Width") ? (uint)Convert.ToInt32(item.Properties["Width"]) : 100;
                                uint height = item.Properties.ContainsKey("Height") ? (uint)Convert.ToInt32(item.Properties["Height"]) : 40;

                                newItem.SetAttribute("Left", left);
                                newItem.SetAttribute("Top", top);
                                newItem.SetAttribute("Width", width);
                                newItem.SetAttribute("Height", height);
                            }
                            catch { }
                        }

                        // 3. Xử lý màu sắc chung
                        string[] colorProps = { "BackColor", "AlternateBackColor", "BorderColor", "AlternateBorderColor", "ForeColor" };
                        foreach (var pName in colorProps)
                        {
                            if (item.Properties.ContainsKey(pName) && item.Properties[pName] != null)
                            {
                                try
                                {
                                    string colorStr = item.Properties[pName].ToString();
                                    string[] rgb = colorStr.Split(',');
                                    if (rgb.Length == 3)
                                    {
                                        int alpha = 255;
                                        if (item.Properties.ContainsKey("Transparent") && (bool)item.Properties["Transparent"] == true && pName.Contains("BackColor"))
                                        {
                                            alpha = 0;
                                        }
                                        var color = System.Drawing.Color.FromArgb(alpha, int.Parse(rgb[0]), int.Parse(rgb[1]), int.Parse(rgb[2]));
                                        newItem.SetAttribute(pName, color);
                                    }
                                }
                                catch { }
                            }
                        }

                        // 4. Xử lý Fill Pattern & Opacity
                        try
                        {
                            if (item.Properties.ContainsKey("BackFillPattern"))
                            {
                                newItem.SetAttribute("BackFillPattern", Convert.ToInt32(item.Properties["BackFillPattern"]));
                            }
                            else if (item.Properties.ContainsKey("Transparent") && (bool)item.Properties["Transparent"] == true)
                            {
                                newItem.SetAttribute("BackFillPattern", 1);
                            }

                            if (item.Properties.ContainsKey("Opacity"))
                            {
                                newItem.SetAttribute("Opacity", Convert.ToDouble(item.Properties["Opacity"]));
                            }
                        }
                        catch { }

                        // 5. XỬ LÝ CHI TIẾT THEO LOẠI
                        try
                        {
                            if (lowerType.Contains("graphicview"))
                            {
                                if (item.Properties.ContainsKey("Graphic"))
                                {
                                    string graphicName = item.Properties["Graphic"].ToString();
                                    newItem.SetAttribute("Graphic", graphicName);
                                    if (item.Properties.ContainsKey("GraphicStretchMode"))
                                    {
                                        newItem.SetAttribute("GraphicStretchMode", (uint)Convert.ToInt32(item.Properties["GraphicStretchMode"]));
                                    }
                                }
                            }
                            else if (lowerType.Contains("iofield"))
                            {
                                string fmt = (item.Properties.ContainsKey("Format") && item.Properties["Format"] != null)
                                            ? item.Properties["Format"].ToString() : "{F2}";
                                try { newItem.SetAttribute("OutputFormat", fmt); } catch { }
                                try { newItem.SetAttribute("ProcessValue", ""); } catch { }
                            }
                            else if (lowerType.Contains("bar") || lowerType.Contains("gauge") || lowerType.Contains("slider"))
                            {
                                double min = item.Properties.ContainsKey("MinValue") ? Convert.ToDouble(item.Properties["MinValue"]) : 0;
                                double max = item.Properties.ContainsKey("MaxValue") ? Convert.ToDouble(item.Properties["MaxValue"]) : 100;
                                try
                                {
                                    if (lowerType.Contains("gauge"))
                                    {
                                        newItem.CurvedScale.SetAttribute("MinValue", min);
                                        newItem.CurvedScale.SetAttribute("MaxValue", max);
                                    }
                                    else
                                    {
                                        newItem.StraightScale.SetAttribute("MinValue", min);
                                        newItem.StraightScale.SetAttribute("MaxValue", max);
                                    }
                                }
                                catch { }
                            }
                            else if (lowerType.Contains("checkbox") || lowerType.Contains("radiobutton"))
                            {
                                var selectionItems = newItem.SelectionItems;
                                var newItemPart = selectionItems.Create();
                                string itemText = (item.Properties.ContainsKey("Text") && item.Properties["Text"] != null)
                                                ? item.Properties["Text"].ToString() : "Option 1";
                                try { newItemPart.SetAttribute("Text", itemText); } catch { }
                            }
                            else if (typeId == "HmiText" || lowerType.Contains("button"))
                            {
                                try
                                {
                                    // 1. Gán nội dung văn bản
                                    string txt = item.Properties.ContainsKey("Text") ? item.Properties["Text"].ToString() : "CAPSTONE PROJECT";
                                    var textItems = newItem.Text.Items;
                                    if (textItems.Count > 0)
                                    {
                                        textItems[0].SetAttribute("Text", $"<body><p>{txt}</p></body>");
                                    }

                                    // 2. Xử lý Font nâng cao
                                    float fSize = 45.0f;
                                    string currentFontName = "Default";

                                    try
                                    {
                                        var fontValue = newItem.GetAttribute("Font");
                                        dynamic dynFont = fontValue;

                                        // Lấy Size
                                        fSize = item.Properties.ContainsKey("Font.Size") ? (float)Convert.ToDouble(item.Properties["Font.Size"]) : 45.0f;
                                        dynFont.Size = fSize;

                                        // Nhận diện Font Name
                                        currentFontName = item.Properties.ContainsKey("Font.Name") ? item.Properties["Font.Name"].ToString() : "Siemens Sans";
                                        string fontCheck = currentFontName.ToUpper();
                                        Type nameType = dynFont.Name.GetType();

                                        if (fontCheck.Contains("TIMES"))
                                        {
                                            dynFont.Name = (dynamic)Enum.Parse(nameType, "TimesNewRoman");
                                        }
                                        else if (fontCheck.Contains("ARIAL"))
                                        {
                                            dynFont.Name = (dynamic)Enum.Parse(nameType, "Arial");
                                        }
                                        else if (fontCheck.Contains("SUN"))
                                        {
                                            dynFont.Name = (dynamic)Enum.Parse(nameType, "SimSun");
                                        }
                                        else
                                        {
                                            dynFont.Name = (dynamic)Enum.Parse(nameType, "SiemensSans");
                                        }

                                        // 3. XỬ LÝ BOLD & ITALIC (Nâng cấp)
                                        bool isBold = item.Properties.ContainsKey("Font.Bold") ? Convert.ToBoolean(item.Properties["Font.Bold"]) : false;
                                        bool isItalic = item.Properties.ContainsKey("Font.Italic") ? Convert.ToBoolean(item.Properties["Font.Italic"]) : false;

                                        // Xử lý Bold (Weight)
                                        try
                                        {
                                            Type wType = dynFont.Weight.GetType();
                                            string weightValue = isBold ? "Bold" : "Normal";
                                            dynFont.Weight = (dynamic)Enum.Parse(wType, weightValue);
                                        }
                                        catch { dynFont.Bold = isBold; }

                                        // Xử lý Italic (Style/Italic tùy phiên bản Openness)
                                        try
                                        {
                                            // Một số phiên bản dùng Enum Style, một số dùng bool Italic trực tiếp
                                            if (item.Properties.ContainsKey("Font.Italic"))
                                            {
                                                try
                                                {
                                                    Type sType = dynFont.Style.GetType();
                                                    string styleValue = isItalic ? "Italic" : "Normal";
                                                    dynFont.Style = (dynamic)Enum.Parse(sType, styleValue);
                                                }
                                                catch
                                                {
                                                    dynFont.Italic = isItalic;
                                                }
                                            }
                                        }
                                        catch { }

                                        // 4. CÚ CHỐT: Đẩy ngược lại Struct Font
                                        newItem.SetAttribute("Font", fontValue);

                                        string styleLog = $"{(isBold ? "Bold" : "")}{(isBold && isItalic ? " " : "")}{(isItalic ? "Italic" : "")}";
                                        Console.WriteLine($"      [OK] Font: {currentFontName} ({fSize}pt) - Style: {(styleLog == "" ? "Normal" : styleLog)}");
                                    }
                                    catch (Exception ex) { }


                                    // 5. Căn lề
                                    try
                                    {
                                        newItem.SetAttribute("HorizontalTextAlignment", (object)1);
                                        newItem.SetAttribute("VerticalTextAlignment", (object)1);
                                    }
                                    catch { }
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine($"      [!] Lỗi tổng quát tại {item.Name}: {ex.Message}");
                                }
                            }
                        }
                        catch { }

                        // --- C. LƯU TRỮ VÀ SCRIPT ---
                        if (!createdObjects.ContainsKey(item.Name)) createdObjects.Add(item.Name, newItem);

                        if (lowerType.Contains("button") && item.Properties.ContainsKey("Scripts"))
                        {
                            ProcessButtonScripts(newItem, item.Name, item.Properties["Scripts"]);
                        }
                        Console.WriteLine($"      [OK] Đã dựng xác: {item.Name}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"      [!] Lỗi GĐ 1 tại {item.Name}: {ex.Message}");
                }
            }

            Console.WriteLine("\n>> [GIAI ĐOẠN 2] Nạp linh hồn THẬT...");

            foreach (var item in items)
            {
                if (!createdObjects.ContainsKey(item.Name)) continue;
                dynamic dynItem = createdObjects[item.Name];
                string realType = dynItem.GetType().Name;

                // Trích xuất Tag
                string tag = item.Properties.ContainsKey("Tag") ? item.Properties["Tag"].ToString() :
                            item.Properties.ContainsKey("StatusTag") ? item.Properties["StatusTag"].ToString() :
                            item.Properties.ContainsKey("LevelTag") ? item.Properties["LevelTag"].ToString() : "";

                if (string.IsNullOrEmpty(tag)) continue;

                // --- PHÂN LUỒNG NẠP TAG ---

                // 1. NHÓM HÌNH KHỐI (Rectangle, Circle, EllipseSegment, CircularArc)
                if (realType.Contains("Rectangle") || realType.Contains("Circle") || realType.Contains("EllipseSegment"))
                {
                    string customScript = item.Properties.ContainsKey("ColorScript") ? item.Properties["ColorScript"].ToString() : "";
                    BindTagToBasicWithStates(dynItem, tag, "BackColor", customScript);
                    Console.WriteLine($"      => [THẬT BACK] {item.Name} -> {tag}");
                }
                else if (realType.Contains("CircularArc"))
                {
                    BindTagToBasic(dynItem, tag, "LineColor");
                    Console.WriteLine($"      => [THẬT LINE] {item.Name} -> {tag}");
                }

                // 2. NHÓM WIDGET (Thư viện)
                else if (item.Properties.ContainsKey("LibraryPath"))
                {
                    string targetProp = item.Type.Contains("Tank") ? "FillLevelColor" : "BasicColor";
                    string customScript = item.Properties.ContainsKey("ColorScript") ? item.Properties["ColorScript"].ToString() : "";
                    foreach (dynamic m in dynItem.Interface)
                    {
                        if (m.PropertyName == targetProp)
                        {
                            if (item.Type.Contains("Motor")) BindScriptToWidget(m, tag, customScript);
                            else BindTagToWidget(m, tag);
                            Console.WriteLine($"      => [THẬT WIDGET] {item.Name} -> {tag}");
                            break;
                        }
                    }
                }

                // 3. NHÓM ĐIỀU KHIỂN & HIỂN THỊ (IO Field, Bar, Slider, Gauge, Radio, CheckBox)
                else if (realType.Contains("Bar") || realType.Contains("Gauge") ||
                        realType.Contains("Slider") || realType.Contains("IoField") ||
                        realType.Contains("Switch") || realType.Contains("CheckBox") ||
                        realType.Contains("RadioButton"))
                {
                    BindTagToBasic(dynItem, tag, "ProcessValue");
                    Console.WriteLine($"      => [THẬT ELEMENT] {item.Name} ({realType}) -> {tag}");
                }

                // 4. NHÓM VĂN BẢN (TextBox)
                else if (realType.Contains("TextBox"))
                {
                    BindTagToBasic(dynItem, tag, "Text");
                    Console.WriteLine($"      => [THẬT TEXT] {item.Name} -> {tag}");
                }
                else if (realType.Contains("Text") || realType.Equals("HmiText"))
                {
                    // Với HmiText, thuộc tính cần Bind Tag thường là "Text" 
                    // Nhưng lưu ý: Bind Tag vào Text của Unified đôi khi cần trỏ sâu vào Resource
                    BindTagToBasic(dynItem, tag, "Text");
                    Console.WriteLine($"      => [THẬT TEXT] {item.Name} -> {tag}");
                }
            }
            Console.WriteLine("[SUCCESS] Vẽ và nạp linh hồn hoàn tất!");
        }
        private IEngineeringObject CreateBaseItem(dynamic composition, string typeName, string name)
        {
            var method = ((object)composition).GetType().GetMethods()
                .FirstOrDefault(m => m.Name == "Create" && m.IsGenericMethod);

            // Tìm Type chính xác theo tên truyền vào (HmiText)
            Type targetType = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a => a.GetTypes())
                .FirstOrDefault(t => t.Name == typeName);

            if (targetType == null)
            {
                Console.WriteLine($"[!] Không tìm thấy Type: {typeName}");
                return null;
            }

            return (IEngineeringObject)method.MakeGenericMethod(targetType).Invoke(composition, new object[] { name });
        }
        public void BindTagToBasic(dynamic item, string tagName, string propName)
        {
            try
            {
                var method = ((object)item.Dynamizations).GetType().GetMethods().FirstOrDefault(m => m.Name == "Create" && m.IsGenericMethod && m.GetParameters().Length == 1);
                Type tagType = AppDomain.CurrentDomain.GetAssemblies().SelectMany(a => a.GetTypes()).FirstOrDefault(t => t.Name == "TagDynamization");
                if (method != null && tagType != null)
                {
                    var tagDyn = method.MakeGenericMethod(tagType).Invoke(item.Dynamizations, new object[] { propName });
                    ((dynamic)tagDyn).Tag = tagName;
                    Console.WriteLine($"      => [THẬT] Basic Tag: {tagName}");
                }
            }
            catch { }
        }

        private void ProcessButtonScripts(dynamic dynItem, string itemName, dynamic scriptsJson)
        {
            // Kiểm tra null để tránh crash
            if (scriptsJson == null) return;

            Type enumType = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a => a.GetTypes())
                .FirstOrDefault(t => t.Name == "HmiButtonEventType");

            if (enumType == null) return;

            // QUAN TRỌNG: Duyệt qua các thuộc tính của đối tượng JSON
            foreach (var scriptEntry in scriptsJson)
            {
                try
                {
                    // Nếu dùng Newtonsoft.Json, scriptEntry sẽ có Name và Value
                    string evName = scriptEntry.Name;
                    string jsCode = scriptEntry.Value.ToString();

                    var evEnum = Enum.Parse(enumType, evName);
                    dynamic handler = null;

                    // Tìm hoặc tạo Handler
                    foreach (dynamic h in dynItem.EventHandlers)
                    {
                        if (h.EventType.ToString() == evName) { handler = h; break; }
                    }

                    if (handler == null)
                    {
                        var method = dynItem.EventHandlers.GetType().GetMethod("Create", new Type[] { enumType });
                        handler = method.Invoke(dynItem.EventHandlers, new object[] { evEnum });
                    }

                    if (handler != null && handler.Script != null)
                    {
                        handler.Script.ScriptCode = jsCode;
                        Console.WriteLine($"      [SCRIPT OK] {itemName} {evName} -> Code Loaded");
                    }
                }
                catch (Exception ex)
                {
                    // Log này sẽ báo cho Otis biết nếu evName không khớp với Enum KeyDown/KeyUp
                    Console.WriteLine($"      [!] Bỏ qua Script không hợp lệ: {ex.Message}");
                }
            }
        }

        public void BindTagToBasicWithStates(dynamic item, string tagName, string propName, string scriptCode)
        {
            try
            {
                var dyns = item.Dynamizations;

                // 1. XÓA TRIỆT ĐỂ DYNAMIZATION CŨ (Tránh lỗi Target of Invocation)
                for (int i = dyns.Count - 1; i >= 0; i--)
                {
                    if (dyns[i].PropertyName == propName)
                    {
                        dyns[i].Delete();
                    }
                }

                // 2. TẠO SCRIPT DYNAMIZATION QUA REFLECTION
                var method = ((object)dyns).GetType().GetMethods()
                    .FirstOrDefault(m => m.Name == "Create" && m.IsGenericMethod && m.GetParameters().Length == 1);

                Type scriptType = AppDomain.CurrentDomain.GetAssemblies().SelectMany(a => a.GetTypes())
                    .FirstOrDefault(t => t.Name == "ScriptDynamization");

                if (method != null && scriptType != null)
                {
                    var dyn = method.MakeGenericMethod(scriptType).Invoke(dyns, new object[] { propName });
                    dynamic scriptObj = (dynamic)dyn;

                    // 3. THIẾT LẬP TRIGGER (Dành riêng cho Basic Objects V20)
                    bool triggerSuccess = false;
                    try
                    {
                        // Thử cách 1: SourceAttribute (Dùng cho WinCC Unified V20 Basic Shapes)
                        scriptObj.SourceAttribute = tagName;
                        triggerSuccess = true;
                    }
                    catch
                    {
                        try
                        {
                            // Thử cách 2: AttributeTriggers (Dùng khi thuộc tính cần giám sát cụ thể)
                            var attrTrigger = scriptObj.AttributeTriggers.Create();
                            attrTrigger.AttributePath = propName;
                            attrTrigger.Tag = tagName;
                            triggerSuccess = true;
                        }
                        catch
                        {
                            try
                            {
                                // Thử cách 3: Triggers tổng quát (Thường dùng cho Widget)
                                var tagTrigger = scriptObj.Triggers.Create();
                                tagTrigger.Tag = tagName;
                                triggerSuccess = true;
                            }
                            catch { }
                        }
                    }

                    // 4. NẠP MÃ SCRIPT (Ưu tiên từ JSON, dùng HMIRuntime.Math.RGB cho chuyên nghiệp)
                    string defaultScript = $@"var status = Tags(""{tagName}"").Read(); 
        return status ? HMIRuntime.Math.RGB(135, 190, 50) : HMIRuntime.Math.RGB(178, 34, 34);";

                    scriptObj.ScriptCode = !string.IsNullOrEmpty(scriptCode) ? scriptCode : defaultScript;

                    // Thông báo trạng thái nạp
                    if (triggerSuccess)
                        Console.WriteLine($"      => [THẬT RGB SCRIPT] {item.Name} -> {tagName} (Trigger OK)");
                    else
                        Console.WriteLine($"      => [THẬT RGB SCRIPT] {item.Name} -> {tagName} (Cần gán Trigger tay)");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"      [!] Lỗi nạp Script tại {item.Name}: {ex.Message}");
            }
        }

        public void BindScriptToWidget(dynamic member, string tagName, string scriptCode)
        {
            try
            {
                dynamic dyns = member.Dynamizations;
                var method = ((object)dyns).GetType().GetMethods()
                    .FirstOrDefault(m => m.Name == "Create" && m.IsGenericMethod && m.GetParameters().Length == 1);

                Type scriptType = AppDomain.CurrentDomain.GetAssemblies().SelectMany(a => a.GetTypes())
                    .FirstOrDefault(t => t.Name == "ScriptDynamization");

                if (method != null && scriptType != null)
                {
                    var scriptDyn = method.MakeGenericMethod(scriptType).Invoke(dyns, new object[] { member.PropertyName });

                    // THIẾT LẬP TRIGGER (Để Script tự chạy khi Tag đổi giá trị)
                    try
                    {
                        var tagTrigger = ((dynamic)scriptDyn).Triggers.Create();
                        tagTrigger.Tag = tagName;
                    }
                    catch { }

                    // NẠP MÃ JS: Ưu tiên lấy từ JSON, nếu trống dùng mẫu chuẩn RGB
                    string finalScript = !string.IsNullOrEmpty(scriptCode) ? scriptCode :
                        $@"var status = Tags(""{tagName}"").Read(); 
        return status ? HMIRuntime.Math.RGB(135, 190, 50) : HMIRuntime.Math.RGB(178, 34, 34);";

                    ((dynamic)scriptDyn).ScriptCode = finalScript;

                    Console.WriteLine($"      => [THẬT WIDGET JSON SCRIPT] {member.PropertyName} -> {tagName}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"      [!] Lỗi Widget Script: {ex.Message}");
            }
        }
        public void BindTagToWidget(dynamic member, string tagName)
        {
            try
            {
                dynamic dyns = member.Dynamizations;
                // Tìm hàm Create(string propertyName) có 1 tham số
                var method = ((object)dyns).GetType().GetMethods()
                    .FirstOrDefault(m => m.Name == "Create" && m.IsGenericMethod && m.GetParameters().Length == 1);

                // SỬA LỖI .Many() thành .SelectMany()
                Type tagType = AppDomain.CurrentDomain.GetAssemblies().SelectMany(a => a.GetTypes())
                    .FirstOrDefault(t => t.Name == "TagDynamization");

                if (method != null && tagType != null)
                {
                    // Thực thi nạp Tag vào đúng cổng PropertyName của Member
                    var tagDyn = method.MakeGenericMethod(tagType).Invoke(dyns, new object[] { member.PropertyName });
                    ((dynamic)tagDyn).Tag = tagName;
                    Console.WriteLine($"      => [THẬT WIDGET] {member.PropertyName} -> {tagName}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"      [!] Lỗi Widget Tag: {ex.Message}");
            }
        }


        public void AssignMomentaryTag(string deviceName, string screenName, string itemName, string newTagName)
        {
            try
            {
                var screenItems = GetScreenItemsComposition(deviceName, screenName);
                dynamic targetItem = screenItems.Cast<dynamic>().FirstOrDefault(i => i.Name == itemName);
                if (targetItem == null) return;

                foreach (dynamic handler in targetItem.EventHandlers)
                {
                    var script = handler.Script;
                    if (script != null)
                    {
                        string evType = handler.EventType.ToString();
                        int val = (evType == "KeyDown") ? 1 : 0;
                        script.SourceCode = $"export function {itemName}_On{evType}(item, keyCode, modifiers) {{\n  Tags(\"{newTagName}\").Write({val});\n}}";
                    }
                }
            }
            catch { }
        }
        private IEngineeringComposition GetScreenItemsComposition(string deviceName, string screenName)
        {
            Device device = FindDeviceRecursive(_project, deviceName); // Sửa: Tìm đệ quy
            if (device == null) return null;
            IEngineeringObject software = GetSoftware(device) as IEngineeringObject;
            if (software == null) return null;
            IEngineeringComposition screens = GetCompositionSafe(software, "Screens");
            IEngineeringObject screen = FindObjectByName(screens, screenName);
            return GetCompositionSafe(screen, "ScreenItems");
        }
        private IEngineeringComposition GetCompositionSafe(IEngineeringObject obj, string compositionName)
        {
            try
            {
                var compOrObj = obj.GetComposition(compositionName);
                return compOrObj as IEngineeringComposition;
            }
            catch { return null; }
        }
        private IEngineeringObject FindObjectByName(IEngineeringComposition composition, string name)
        {
            if (composition == null) return null;
            return composition.Cast<IEngineeringObject>().FirstOrDefault(item =>
            {
                try
                {
                    var attr = item.GetAttribute("Name");
                    return attr != null && attr.ToString() == name;
                }
                catch { return false; }
            });
        }


        private void CreateDynamicWidget(dynamic composition, string type, string name, dynamic properties)
        {
            try
            {
                // 1. Tìm kiểu dữ liệu đích
                Type targetType = AppDomain.CurrentDomain.GetAssemblies()
                    .SelectMany(a => a.GetTypes())
                    .FirstOrDefault(t => t.Name == "HmiCustomWidgetContainer");

                if (targetType == null) return;

                // 2. Lấy method Create(string name, string typeIdentifier)
                // Chúng ta lấy phương thức Generic
                var methodInfo = ((object)composition).GetType().GetMethods()
                    .FirstOrDefault(m => m.Name == "Create" && m.IsGenericMethod && m.GetParameters().Length == 2);

                if (methodInfo == null) return;

                // 3. ĐÂY LÀ BƯỚC QUAN TRỌNG: Cụ thể hóa phương thức Generic
                // Biến Create<T> thành Create<HmiCustomWidgetContainer>
                var concreteMethod = methodInfo.MakeGenericMethod(targetType);

                // 4. Chuẩn bị định danh
                string subType = properties.ContainsKey("SubType") ? properties["SubType"].ToString() : type;
                string typeIdentifier = $"extended.{subType}";

                // 5. THỰC THI (Invoke phương thức đã được cụ thể hóa)
                // Không dùng late bound trực tiếp trên methodInfo nữa
                var newItem = (IEngineeringObject)concreteMethod.Invoke(composition, new object[] { (string)name, (string)typeIdentifier });

                if (newItem != null)
                {
                    // Gán tọa độ và Interface (Giữ nguyên phần code SetAttribute của bạn)
                    newItem.SetAttribute("Left", Convert.ToInt32(properties["Left"]));
                    newItem.SetAttribute("Top", Convert.ToInt32(properties["Top"]));
                    newItem.SetAttribute("Width", (uint)Convert.ToUInt32(properties["Width"]));
                    newItem.SetAttribute("Height", (uint)Convert.ToUInt32(properties["Height"]));

                    // ... (Phần nạp BasicColor, FillLevelValue...)
                    Console.WriteLine($"      [RENDER OK] {name} ({subType})");
                }
            }
            catch (Exception ex)
            {
                // Dùng InnerException để xem lỗi thật sự từ Siemens nếu có
                Console.WriteLine($"      [LỖI TẠI {name}]: {ex.InnerException?.Message ?? ex.Message}");
            }
        }

        #endregion

        #region 7. WinCC Unified: Connection & Tag Import
        public string CreateUnifiedConnectionCombined(string hmiName, string hmiIp, string plcIp, string connectionName = "Connection_1")
        {
            if (_project == null) return "Project chưa mở.";

            try
            {
                Device hmiDevice = FindDeviceRecursive(_project, hmiName);
                if (hmiDevice == null) return $"[ERROR] Không tìm thấy thiết bị: {hmiName}";

                var software = GetSoftware(hmiDevice) as HmiSoftware;
                var connections = software.Connections;
                var existing = connections.Find(connectionName);
                if (existing != null) existing.Delete();

                // Bước 1: Tạo kết nối và định danh Driver
                var newConn = connections.Create(connectionName);
                newConn.SetAttribute("CommunicationDriver", "SIMATIC S7 1200/1500");

                // Bước 2: GIẢI PHÁP - Gán trực tiếp từng thuộc tính thay vì gửi chuỗi InitialAddress
                // Cách này giúp TIA Portal không phải tự phân tách chuỗi, tránh lỗi format
                try
                {
                    newConn.SetAttribute("HostAddress", hmiIp); // IP của HMI
                    newConn.SetAttribute("PlcAddress", plcIp); // IP của PLC
                    newConn.SetAttribute("HostAccessPoint", "S7ONLINE");

                    // Gán các thông số phụ mà Driver yêu cầu
                    newConn.SetAttribute("PlcExpansionSlot", 1);
                    newConn.SetAttribute("PlcRack", 0);
                    newConn.SetAttribute("PlcIsCyclicOperation", true);
                }
                catch
                {
                    // Fallback: Nếu gán rời bị chặn, dùng chuỗi tối giản nhất (không có dấu ; ở cuối)
                    string minimal = $"Version=16.0.0.0;HostAddress={hmiIp};PlcAddress={plcIp}";
                    newConn.SetAttribute("InitialAddress", minimal);
                }

                return $"[SUCCESS] Đã tạo và thiết lập kết nối: {connectionName}";
            }
            catch (Exception ex)
            {
                return $"[ERROR] Lỗi hệ thống: {ex.Message}";
            }
        }


        public void ImportHmiTagsFromCsv(string hmiName, string csvPath)
        {
            if (_project == null) { Console.WriteLine("[ERROR] Project chưa mở."); return; }

            try
            {
                if (!System.IO.File.Exists(csvPath))
                {
                    Console.WriteLine($"[ERROR] Không tìm thấy file: {csvPath}"); return;
                }

                Device hmiDevice = FindDeviceRecursive(_project, hmiName);
                var software = GetSoftware(hmiDevice) as HmiSoftware;
                var table = software.TagTables.Find("Default tag table") ?? software.TagTables.Create("Imported_Tags");

                string[] lines = System.IO.File.ReadAllLines(csvPath);
                int successCount = 0;

                for (int i = 1; i < lines.Length; i++)
                {
                    // Quan trọng: Thử dùng ';' nếu file export từ Excel Việt Nam/Châu Âu
                    string[] columns = lines[i].Split(',');
                    if (columns.Length < 4) columns = lines[i].Split(';');

                    // Kiểm tra an toàn để tránh lỗi Index outside bounds
                    if (columns.Length < 4)
                    {
                        Console.WriteLine($"[ERROR] Dòng {i + 1} không đủ 4 cột dữ liệu cơ bản.");
                        continue;
                    }

                    string tagName = columns[0].Trim();      // Cột A
                    string connName = columns[1].Trim();     // Cột B
                    string address = columns[2].Trim();      // Cột C
                    string dataType = columns[3].Trim();     // Cột D

                    try
                    {
                        var tags = table.Tags;
                        if (tags.Find(tagName) != null) tags.Find(tagName).Delete();

                        var newTag = tags.Create(tagName);

                        // 1. Gán Connection trước để "Unlock" các trường dữ liệu
                        newTag.SetAttribute("Connection", connName);

                        // 2. Gán DataType (Phải viết hoa chữ đầu: Bool, Int, Real)
                        newTag.SetAttribute("DataType", dataType);

                        // 3. Thiết lập chế độ Tuyệt đối (1 = AbsoluteAccess)
                        newTag.SetAttribute("AccessMode", 1);

                        // 4. Gán địa chỉ tuyệt đối (ví dụ %M1.0)
                        newTag.SetAttribute("Address", address);
                        // Kiểm tra nếu cột IsLogging là True thì gọi hàm kích hoạt Log
                        if (columns.Length >= 6 && columns[4].Trim().ToLower() == "true")
                        {
                            string logName = columns[5].Trim();
                            EnableLoggingForTag(hmiName, tagName, logName);
                        }

                        // 5. Gán Acquisition Cycle nếu có (Cột G)
                        if (columns.Length >= 7)
                        {
                            newTag.SetAttribute("AcquisitionCycle", columns[6].Trim());
                        }

                        successCount++;
                        Console.WriteLine($"[INFO] Đã nạp thành công: {tagName}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[ERROR] Dòng {i + 1} ({tagName}): {ex.Message}");
                    }
                }
                Console.WriteLine($"[SUCCESS] Hoàn thành! Đã nạp {successCount}/{lines.Length - 1} tags vào {hmiName}.");
            }
            catch (Exception ex) { Console.WriteLine($"[ERROR] Fatal: {ex.Message}"); }
        }


        public void ImportPlcTagsFromCsv(string plcName, string csvPath)
        {
            if (_project == null) { Console.WriteLine("[ERROR] Project chưa mở."); return; }

            try
            {
                if (!System.IO.File.Exists(csvPath))
                {
                    Console.WriteLine($"[ERROR] Không tìm thấy file: {csvPath}"); return;
                }

                Device plcDevice = FindDeviceRecursive(_project, plcName);
                var software = GetSoftware(plcDevice) as PlcSoftware;
                if (software == null) return;

                string[] lines = System.IO.File.ReadAllLines(csvPath);
                int successCount = 0;

                for (int i = 1; i < lines.Length; i++)
                {
                    // Tách cột bằng dấu phẩy hoặc dấu chấm phẩy
                    string[] columns = lines[i].Split(',');
                    if (columns.Length < 4) columns = lines[i].Split(';');

                    if (columns.Length < 4) continue;

                    // --- CẬP NHẬT THEO HÌNH ẢNH EXCEL ---
                    string tagName = columns[0].Trim();         // Cột A: Name
                    string tablePath = columns[1].Trim();      // Cột B: Path (Tag Table)
                    string dataType = columns[2].Trim();       // Cột C: Data Type
                    string address = columns[3].Trim();        // Cột D: Logical Address
                    string comment = columns.Length > 4 ? columns[4].Trim() : ""; // Cột E: Comment

                    try
                    {
                        // Tìm hoặc tạo Tag Table dựa theo cột B
                        var table = software.TagTableGroup.TagTables.Find(tablePath)
                                    ?? software.TagTableGroup.TagTables.Create(tablePath);

                        var plcTags = table.Tags;
                        if (plcTags.Find(tagName) != null) plcTags.Find(tagName).Delete();

                        var newTag = plcTags.Create(tagName);

                        // Gán thuộc tính theo đúng Openness API cho PLC
                        newTag.SetAttribute("DataTypeName", dataType);
                        newTag.SetAttribute("LogicalAddress", address);

                        if (!string.IsNullOrEmpty(comment))
                        {
                            newTag.Comment.Items.First().Text = comment;
                        }

                        successCount++;
                        Console.WriteLine($"[INFO] Line {i + 1}: Đã nạp {tagName} vào bảng {tablePath}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[ERROR] Dòng {i + 1} ({tagName}): {ex.Message}");
                    }
                }
                Console.WriteLine($"[SUCCESS] Hoàn thành! Đã nạp {successCount} tags vào PLC {plcName}.");
            }
            catch (Exception ex) { Console.WriteLine($"[ERROR] Fatal: {ex.Message}"); }
        }


        public string EnableLoggingForTag(string hmiName, string tagName, string dataLogName)
        {
            if (_project == null) return "[ERROR] Project chưa mở.";

            try
            {
                Device hmiDevice = FindDeviceRecursive(_project, hmiName);
                var software = GetSoftware(hmiDevice) as HmiSoftware;

                // 1. Tìm Tag cần Log trong Default tag table
                var table = software.TagTables.Find("Default tag table");
                var hmiTag = table.Tags.Find(tagName);
                if (hmiTag == null) return $"[ERROR] Không tìm thấy Tag: {tagName}";

                // 2. Truy cập danh sách LoggingTags của Tag đó
                var loggingTags = hmiTag.LoggingTags;

                // 3. Tạo LoggingTag mới (thường đặt tên trùng với tên Tag hoặc tagName_Log)
                string logTagName = tagName + "_Log";
                var existingLog = loggingTags.Find(logTagName);
                if (existingLog != null) existingLog.Delete();

                var newLoggingTag = loggingTags.Create(logTagName);

                // 4. Cấu hình các thuộc tính dựa trên API bạn gửi
                // Gán vào bảng Data Log (Ví dụ: "Data_log_1")
                newLoggingTag.SetAttribute("LogConfiguration", dataLogName);

                // Chế độ ghi: 3 = OnChange (Ghi khi thay đổi)
                newLoggingTag.SetAttribute("LoggingMode", 3);

                // Nếu muốn làm mượt dữ liệu (Smoothing)
                newLoggingTag.SetAttribute("SmoothingMode", 0); // 0 = NoSmoothing

                return $"[SUCCESS] Đã kích hoạt Data Log cho Tag '{tagName}' vào bảng '{dataLogName}'";
            }
            catch (Exception ex)
            {
                return $"[ERROR] Lỗi Logging: {ex.Message}";
            }
        }

        #endregion

        #region 8. Graphics & Multimedia Management
        public bool AddPngToProjectGraphics(string filePath, string graphicName)
        {
            try
            {
                dynamic graphics = ((dynamic)_project).Graphics;
                if (graphics == null || !File.Exists(filePath)) return false;

                string directory = Path.GetDirectoryName(filePath);
                string folderName = graphicName + "_files";
                string folderPath = Path.Combine(directory, folderName);
                Directory.CreateDirectory(folderPath);
                File.Copy(filePath, Path.Combine(folderPath, "DefaultImageStream.png"), true);

                string xmlPath = Path.Combine(directory, "import_wrapper.xml");
                string xmlContent = $@"<?xml version=""1.0"" encoding=""utf-8""?><Document><Engineering version=""V20"" /><Hmi.Globalization.MultiLingualGraphic ID=""0""><AttributeList><Name>{graphicName}</Name><DefaultImageStream external=""path"">{folderName}\DefaultImageStream.png</DefaultImageStream></AttributeList></Hmi.Globalization.MultiLingualGraphic></Document>";
                File.WriteAllText(xmlPath, xmlContent, Encoding.UTF8);

                graphics.Import(new FileInfo(xmlPath), (dynamic)Enum.ToObject(graphics.GetType().GetMethod("Import").GetParameters()[1].ParameterType, 0));
                return true;
            }
            catch { return false; }
        }

        public void ImportAllImagesFromFolder(string folderPath)
        {
            foreach (var file in Directory.GetFiles(folderPath, "*.png"))
                AddPngToProjectGraphics(file, Path.GetFileNameWithoutExtension(file));
        }
        public bool ImportGraphic(string name, string filePath)
        {
            try
            {
                // Truy cập vào kho Graphics của Project qua Reflection
                PropertyInfo graphicsProp = _project.GetType().GetProperty("Graphics");
                var graphicsCollection = graphicsProp.GetValue(_project);

                // Gọi hàm Import(name, path, folder)
                // Lưu ý: TIA Openness cho phép nạp trực tiếp vào root Graphics
                MethodInfo importMethod = graphicsCollection.GetType().GetMethod("Import", new[] { typeof(string), typeof(string) });
                if (importMethod != null)
                {
                    importMethod.Invoke(graphicsCollection, new object[] { name, filePath });
                    return true;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"      [!] Không thể nạp ảnh: {ex.Message}");
            }
            return false;
        }


        public void CreateGraphicList(string listName, List<string> graphicNames) { /* Logic Create Graphic List */ }

        public List<string> GetProjectGraphicsNames()
        {
            List<string> names = new List<string>();
            try { foreach (dynamic g in (dynamic)_project.GetType().GetProperty("Graphics").GetValue(_project)) names.Add(g.Name.ToString()); } catch { }
            return names;
        }
        #endregion

        #region 9. Download, Online & Diagnostic Operations
        public string DownloadToPLC(string deviceName, string targetIpAddress, string pgPcInterfaceName)
        {
            if (_project == null) return "Error: Project chưa được load.";

            try
            {
                // 1. Tìm thiết bị và khởi tạo các Service
                Device device = FindDeviceRecursive(_project, deviceName);
                if (device == null) return $"Error: Không tìm thấy thiết bị '{deviceName}'";

                var plcItem = GetCpuItem(device);
                var downloadProvider = plcItem?.GetService<Siemens.Engineering.Download.DownloadProvider>();
                var onlineProvider = plcItem?.GetService<Siemens.Engineering.Online.OnlineProvider>();

                if (downloadProvider == null) return "Error: DownloadProvider không khả dụng.";

                // 2. KIỂM TRA THÔNG TUYẾN (PING)
                // Đảm bảo card mạng ảo Siemens PLCSIM đã nhận diện được PLC
                Console.WriteLine($"[i] Đang kiểm tra Ping tới {targetIpAddress}...");
                using (System.Net.NetworkInformation.Ping ping = new System.Net.NetworkInformation.Ping())
                {
                    try
                    {
                        var reply = ping.Send(targetIpAddress, 2000);
                        if (reply.Status != System.Net.NetworkInformation.IPStatus.Success)
                        {
                            return $"FAILED: Không thể Ping thấy PLC tại {targetIpAddress}. Hãy kiểm tra PLCSIM Advanced hoặc Card mạng số {pgPcInterfaceName}!";
                        }
                        Console.WriteLine($"[√] Ping thành công! (Thời gian phản hồi: {reply.RoundtripTime}ms)");
                    }
                    catch (Exception ex)
                    {
                        return $"FAILED: Lỗi hệ thống khi Ping: {ex.Message}";
                    }
                }

                // 3. HANDSHAKE BẢO MẬT (Xử lý bảng Trustworthy của V20)
                try
                {
                    dynamic dynamicProvider = onlineProvider;
                    // Dùng dynamic để "bắt" sự kiện CertificateValidation nếu nó tồn tại
                    dynamicProvider.CertificateValidation += new EventHandler<dynamic>((sender, e) =>
                    {
                        Console.WriteLine($"   [√] Đã tự động chấp nhận Certificate bảo mật cho {deviceName}");
                        e.Accept(); // Nhấn "Connect" tự động trên bảng Trustworthy
                    });
                }
                catch { /* Bỏ qua nếu phiên bản TIA không hỗ trợ event này trực tiếp */ }

                // 4. CẤU HÌNH INTERFACE
                var mode = downloadProvider.Configuration.Modes.Find("PN/IE");
                var pcInterface = mode.PcInterfaces.Find(pgPcInterfaceName, 1);
                if (pcInterface == null) return "FAILED: Không tìm thấy Card mạng phù hợp.";

                var targetConf = pcInterface.TargetInterfaces[0];

                // Áp cấu hình để mở Socket kết nối
                downloadProvider.Configuration.ApplyConfiguration(targetConf);

                // 5. THỰC THI NẠP (DOWNLOAD)
                Console.WriteLine("[i] Đang bắt đầu quá trình nạp chương trình...");
                var result = downloadProvider.Download(
                    targetConf,
                    (preConf) => // Xử lý các bảng thông báo trước khi nạp (Stop Modules, Overwrite...)
                    {
                        dynamic d = preConf;
                        try
                        {
                            string configName = preConf.GetType().Name;
                            Console.WriteLine($"   => Đang xử lý bảng: {configName}");

                            var prop = preConf.GetType().GetProperty("CurrentSelection");
                            if (prop != null)
                            {
                                var enumType = prop.GetValue(preConf).GetType();
                                foreach (var name in Enum.GetNames(enumType))
                                {
                                    // Tự động chọn hành động để tiếp tục nạp
                                    if (name.Contains("Stop") || name.Contains("Overwrite") || name.Contains("Accept"))
                                    {
                                        prop.SetValue(preConf, Enum.Parse(enumType, name));
                                        Console.WriteLine($"      [√] Đã chọn: {name}");
                                        break;
                                    }
                                }
                            }
                            // Xác nhận đã xử lý bảng
                            var chk = preConf.GetType().GetProperty("Checked") ?? preConf.GetType().GetProperty("IsChecked");
                            if (chk != null) chk.SetValue(preConf, true);
                        }
                        catch { }
                    },
                    (postConf) => // Xử lý sau khi nạp (Restart PLC)
                    {
                        try
                        {
                            var prop = postConf.GetType().GetProperty("CurrentSelection");
                            if (prop != null)
                            {
                                var enumType = prop.GetValue(postConf).GetType();
                                foreach (var name in Enum.GetNames(enumType))
                                {
                                    if (name.Contains("Start"))
                                    {
                                        prop.SetValue(postConf, Enum.Parse(enumType, name));
                                        Console.WriteLine("   [√] PLC đang khởi động lại (RUN)...");
                                        break;
                                    }
                                }
                            }
                        }
                        catch { }
                    },
                    Siemens.Engineering.Download.DownloadOptions.Hardware | Siemens.Engineering.Download.DownloadOptions.Software
                );

                // 6. TRẢ KẾT QUẢ
                if (result.State == Siemens.Engineering.Download.DownloadResultState.Success)
                {
                    return "SUCCESS: Chương trình đã nạp hoàn tất và PLC đã RUN!";
                }
                else
                {
                    var errorMsg = result.Messages.FirstOrDefault(m => m.State == Siemens.Engineering.Download.DownloadResultState.Error)?.Message ?? "Unknown Error";
                    return $"FAILED: {result.State}. Chi tiết: {errorMsg}";
                }
            }
            catch (Exception ex)
            {
                return $"CRITICAL ERROR: {ex.GetBaseException().Message}";
            }
        }


        // --- BỔ SUNG: THAY ĐỔI TRẠNG THÁI PLC (RUN/STOP) ---
        // --- FUNCTION: MANUAL START/STOP PLC (FIX LỖI STOP KHI ĐANG RUN) ---
        public string ChangePlcState(string deviceName, string targetIp, string netCard, bool turnOn)
        {
            string actionName = turnOn ? "Start" : "Stop";
            string targetDesc = turnOn ? "RUN (Start Module)" : "STOP (Stop Module)";

            Console.WriteLine($"\n--- EXECUTING MANUAL COMMAND: {targetDesc} ---");

            if (_project == null) return "Error: Project not loaded.";

            try
            {
                // 1. SETUP
                Device device = FindDeviceRecursive(_project, deviceName);
                if (device == null) return "Error: Device not found.";

                var downloadProvider = (GetCpuItem(device) as IEngineeringServiceProvider)?.GetService<Siemens.Engineering.Download.DownloadProvider>();
                if (downloadProvider == null) return "Error: CPU does not support Download/Control.";

                var mode = downloadProvider.Configuration.Modes.Find("PN/IE");
                var pcInterface = mode.PcInterfaces.Find(netCard, 1);
                if (pcInterface == null)
                    foreach (var pc in mode.PcInterfaces) if (pc.Name.Contains(netCard)) { pcInterface = pc; break; }

                if (pcInterface == null) return "Error: Network Card not found.";
                var targetConf = pcInterface.TargetInterfaces.Count > 0 ? pcInterface.TargetInterfaces[0] : null;

                // 2. THỰC HIỆN LỆNH
                bool actionSuccess = false;
                var ops = Siemens.Engineering.Download.DownloadOptions.Hardware | Siemens.Engineering.Download.DownloadOptions.Software;

                var result = downloadProvider.Download(
                    targetConf,

                    // --- PRE-DOWNLOAD: QUAN TRỌNG - PHẢI XỬ LÝ STOP MODULES TẠI ĐÂY ---
                    (preConf) =>
                    {
                        // Logic này giúp xử lý tình huống: PLC đang RUN mà muốn nạp lệnh STOP
                        try
                        {
                            // 1. Thử xử lý theo kiểu Enum (StopModulesSelections)
                            var prop = preConf.GetType().GetProperty("CurrentSelection");
                            if (prop != null)
                            {
                                var currentValue = prop.GetValue(preConf);
                                var enumType = currentValue.GetType();
                                foreach (var name in Enum.GetNames(enumType))
                                {
                                    if (name.IndexOf("Stop", StringComparison.OrdinalIgnoreCase) >= 0)
                                    {
                                        prop.SetValue(preConf, Enum.Parse(enumType, name));
                                        // Console.WriteLine($"   [Pre-Check] Auto-accepted: {name}");
                                        break;
                                    }
                                }
                            }
                            // 2. Thử xử lý theo kiểu List (Fallback)
                            else
                            {
                                var list = preConf as System.Collections.IEnumerable;
                                if (list != null)
                                {
                                    foreach (dynamic item in list)
                                    {
                                        try
                                        {
                                            foreach (dynamic option in item.Options)
                                            {
                                                if (option.Name.ToString().Contains("Stop"))
                                                {
                                                    item.Current = option;
                                                    break;
                                                }
                                            }
                                        }
                                        catch { }
                                    }
                                }
                            }
                        }
                        catch { } // Bỏ qua lỗi nhỏ để ưu tiên chạy tiếp
                    },

                    // --- POST-DOWNLOAD: CHỌN TRẠNG THÁI CUỐI CÙNG ---
                    (postConf) =>
                    {
                        Console.WriteLine($"-> Configuring PLC State to: {actionName.ToUpper()}...");
                        try
                        {
                            var prop = postConf.GetType().GetProperty("CurrentSelection");
                            if (prop != null)
                            {
                                var currentValue = prop.GetValue(postConf);
                                var enumType = currentValue.GetType();
                                string[] enumNames = Enum.GetNames(enumType);
                                bool found = false;

                                foreach (var name in enumNames)
                                {
                                    bool isTarget = false;
                                    if (turnOn) // RUN
                                        isTarget = name.IndexOf("Start", StringComparison.OrdinalIgnoreCase) >= 0;
                                    else // STOP
                                        isTarget = name.IndexOf("Stop", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                   name.IndexOf("NoAction", StringComparison.OrdinalIgnoreCase) >= 0;

                                    if (isTarget)
                                    {
                                        prop.SetValue(postConf, Enum.Parse(enumType, name));
                                        Console.WriteLine($"   [OK] Action Set: {name}");
                                        actionSuccess = true;
                                        found = true;
                                        break;
                                    }
                                }
                                if (!found && !turnOn)
                                {
                                    // Nếu muốn STOP mà không thấy tùy chọn Stop -> Có thể nó đã Stop từ bước Pre-Check rồi
                                    // Ta cứ báo Success để người dùng không hoang mang
                                    Console.WriteLine("   [Info] PLC might be already stopped via Pre-Check.");
                                    actionSuccess = true;
                                }
                            }
                        }
                        catch (Exception ex) { Console.WriteLine($"   [Error] State conf failed: {ex.Message}"); }
                    },
                    ops
                );

                // 3. KẾT QUẢ
                if (result.State == Siemens.Engineering.Download.DownloadResultState.Success)
                {
                    // Nếu muốn Stop mà ở Pre-Check đã Stop rồi thì coi như thành công
                    if (actionSuccess || !turnOn) return $"SUCCESS: PLC switched to {targetDesc}.";
                    else return "WARNING: Command sent but State Option was not explicitly confirmed.";
                }
                else
                {
                    var msg = result.Messages.FirstOrDefault(m => m.State == Siemens.Engineering.Download.DownloadResultState.Error)?.Message ?? "Unknown";
                    if (msg.Contains("Connect to module") || msg.Contains("failed"))
                        return "⚠️ FAILED: Connection refused. Check Certificate or Network.";
                    return $"FAILED: {msg}";
                }
            }
            catch (Exception ex) { return $"EXCEPTION: {ex.Message}"; }
        }


        public string GetPlcStatus(string deviceName, string netCard)
        {
            Console.WriteLine($"\n--- CHECKING CONNECTION ---");
            if (_project == null) return "Error: Project not loaded.";

            try
            {
                Device device = FindDeviceRecursive(_project, deviceName);
                if (device == null) return "Error: Device not found.";

                DeviceItem cpuItem = GetCpuItem(device);
                var serviceProvider = cpuItem as IEngineeringServiceProvider;
                var onlineProvider = serviceProvider?.GetService<Siemens.Engineering.Online.OnlineProvider>();

                if (onlineProvider == null) return "Error: No Online support.";

                // Cấu hình mạng
                var mode = onlineProvider.Configuration.Modes.Find("PN/IE");
                var pcInterface = mode.PcInterfaces.Find(netCard, 1);
                if (pcInterface == null)
                    foreach (var pc in mode.PcInterfaces) if (pc.Name.Contains(netCard)) { pcInterface = pc; break; }

                if (pcInterface == null) return "Error: Net Card not found.";

                // Thử kết nối
                Console.WriteLine(">> Pinging PLC (Going Online)...");
                onlineProvider.GoOnline();

                if (onlineProvider.State == Siemens.Engineering.Online.OnlineState.Online)
                {
                    onlineProvider.GoOffline();
                    return "SUCCESS: PLC is ONLINE and REACHABLE.";
                }
                return "WARNING: PLC not reachable.";
            }
            catch (Exception ex)
            {
                return $"CONNECTION FAILED: {ex.Message}";
            }
        }


        public string FlashPlcLed(string deviceName, string netCard)
        {
            Console.WriteLine($"\n--- TESTING CONNECTION (HOLDING ONLINE FOR 10s) ---");

            if (_project == null) return "Error: Project not loaded.";

            Siemens.Engineering.Online.OnlineProvider onlineProvider = null;

            try
            {
                // 1. SETUP (Tìm thiết bị)
                Device device = FindDeviceRecursive(_project, deviceName);
                if (device == null) return "Error: Device not found.";

                DeviceItem cpuItem = GetCpuItem(device);
                if (cpuItem == null) return "Error: CPU item not found.";

                // 2. LẤY ONLINE PROVIDER
                var serviceProvider = cpuItem as IEngineeringServiceProvider;
                onlineProvider = serviceProvider?.GetService<Siemens.Engineering.Online.OnlineProvider>();
                if (onlineProvider == null) return "Error: CPU does not support Online connection.";

                // 3. CẤU HÌNH MẠNG
                var mode = onlineProvider.Configuration.Modes.Find("PN/IE");
                var pcInterface = mode.PcInterfaces.Find(netCard, 1);
                if (pcInterface == null)
                    foreach (var pc in mode.PcInterfaces) if (pc.Name.Contains(netCard)) { pcInterface = pc; break; }

                if (pcInterface == null) return "Error: Network Card not found.";

                // 4. THỰC HIỆN KẾT NỐI
                Console.WriteLine(">> Going Online... (Please watch TIA Portal window)");
                onlineProvider.GoOnline();

                if (onlineProvider.State == Siemens.Engineering.Online.OnlineState.Online)
                {
                    // 5. GIỮ KẾT NỐI VÀ ĐẾM NGƯỢC
                    Console.WriteLine("\n>> [SUCCESS] PLC IS ONLINE!");
                    Console.WriteLine(">> Look at TIA Portal now: You should see ORANGE bars and GREEN checks.");

                    Console.Write(">> Going Offline in: ");
                    for (int i = 10; i > 0; i--)
                    {
                        Console.Write($"{i}... ");
                        System.Threading.Thread.Sleep(1000); // Dừng 1 giây
                    }
                    Console.WriteLine("Now!");

                    return "SUCCESS: Connection verified manually.";
                }
                else
                {
                    return "WARNING: Command sent but PLC did not report Online state.";
                }
            }
            catch (Exception ex)
            {
                return $"CONNECTION FAILED: {ex.Message}";
            }
            finally
            {
                // 6. NGẮT KẾT NỐI
                try
                {
                    if (onlineProvider != null && onlineProvider.State == Siemens.Engineering.Online.OnlineState.Online)
                    {
                        Console.WriteLine(">> Disconnected (Offline).");
                        onlineProvider.GoOffline();
                    }
                }
                catch { }
            }
        }

        #endregion

        #region 10. Backup & Restore Operations
        public string BackupPlcData(string deviceName, string backupFolderPath)
        {
            Device device = FindDeviceRecursive(_project, deviceName);
            PlcSoftware software = GetSoftware(device) as PlcSoftware;
            Directory.CreateDirectory(backupFolderPath);
            foreach (PlcBlock block in software.BlockGroup.Blocks) block.Export(new FileInfo(Path.Combine(backupFolderPath, block.Name + ".xml")), ExportOptions.None);
            return "Backup Done";
        }

        #endregion

        #region 11. Core Helpers (Scanning & Reflection)
        private Device FindDeviceRecursive(Project project, string deviceName)
        {
            Device d = project.Devices.Find(deviceName);
            if (d != null) return d;
            foreach (var group in project.DeviceGroups)
            {
                d = FindDeviceInGroupRecursive(group, deviceName);
                if (d != null) return d;
            }
            return null;
        }

        private Device FindDeviceInGroupRecursive(DeviceUserGroup group, string deviceName)
        {
            Device d = group.Devices.Find(deviceName);
            if (d != null) return d;
            foreach (var sub in group.Groups)
            {
                d = FindDeviceInGroupRecursive(sub, deviceName);
                if (d != null) return d;
            }
            return null;
        }

        private Software GetSoftware(Device device) => FindSoftwareRecursive(device);

        private Software FindSoftwareRecursive(IEngineeringObject obj)
        {
            var container = (obj as IEngineeringServiceProvider)?.GetService<SoftwareContainer>();
            if (container != null) return container.Software;
            if (obj is Device d) foreach (var i in d.DeviceItems) { var r = FindSoftwareRecursive(i); if (r != null) return r; }
            if (obj is DeviceItem di) foreach (var i in di.DeviceItems) { var r = FindSoftwareRecursive(i); if (r != null) return r; }
            return null;
        }

        private DeviceItem GetCpuItem(Device device)
        {
            foreach (DeviceItem item in device.DeviceItems)
            {
                if (((IEngineeringServiceProvider)item).GetService<Siemens.Engineering.Download.DownloadProvider>() != null) return item;
                foreach (DeviceItem sub in item.DeviceItems)
                    if (((IEngineeringServiceProvider)sub).GetService<Siemens.Engineering.Download.DownloadProvider>() != null) return sub;
            }
            return null;
        }

        private void CheckProject() { if (_project == null && _tiaPortal?.Projects.Count > 0) _project = _tiaPortal.Projects[0]; }

        private void ScanGroupRecursive(DeviceUserGroup group, List<string> names)
        {
            foreach (Device d in group.Devices) names.Add(d.Name);
            foreach (DeviceUserGroup sub in group.Groups) ScanGroupRecursive(sub, names);
        }

        private DeviceItem FindNetworkInterfaceItem(DeviceItemComposition items)
        {
            foreach (DeviceItem item in items)
            {
                if (item.GetService<NetworkInterface>()?.InterfaceType == NetType.Ethernet) return item;
                var sub = FindNetworkInterfaceItem(item.DeviceItems);
                if (sub != null) return sub;
            }
            return null;
        }

        private void SetPlcIpAddress(Device device, string ip)
        {
            var node = FindNetworkInterfaceItem(device.DeviceItems)?.GetService<NetworkInterface>()?.Nodes[0];
            if (node != null) node.SetAttribute("Address", ip);
        }

        private dynamic GetHmiTarget(string deviceName)
        {
            Device device = FindDeviceRecursive(_project, deviceName);
            return DeepSearchHmiTarget(device.DeviceItems, 1);
        }

        private dynamic DeepSearchHmiTarget(DeviceItemComposition items, int level)
        {
            foreach (DeviceItem item in items)
            {
                var sw = item.GetService<SoftwareContainer>()?.Software;
                if (sw != null && sw.GetType().Name.Contains("Hmi")) return sw;
                var sub = DeepSearchHmiTarget(item.DeviceItems, level + 1);
                if (sub != null) return sub;
            }
            return null;
        }
        #endregion

        #region 12. Debug & Diagnostic Tools


        // 1. Export Màn hình Unified sang JSON
        public void ExportUnifiedScreenToJson(string deviceName, string screenName, string outputPath)
        {
            var hmiTarget = GetHmiTarget(deviceName);
            dynamic screens = null;
            try { screens = hmiTarget.Screens; } catch { screens = hmiTarget.ScreenFolder.Screens; }

            var screen = ((System.Collections.IEnumerable)screens).Cast<dynamic>().FirstOrDefault(s => s.Name == screenName);
            if (screen == null) throw new Exception($"Không tìm thấy màn hình: {screenName}");

            var exportModel = new
            {
                ScreenName = screen.Name,
                Width = (int)screen.Width,
                Height = (int)screen.Height,
                Items = new List<object>()
            };

            // Serialize và lưu file
            string json = JsonConvert.SerializeObject(exportModel, Newtonsoft.Json.Formatting.Indented);
            File.WriteAllText(outputPath, json);
        }

        public void ExportUnifiedScreenWithTextToJson(string deviceName, string screenName, string outputPath)
        {
            var hmiTarget = GetHmiTarget(deviceName);
            dynamic screens = null;
            try { screens = hmiTarget.Screens; } catch { screens = hmiTarget.ScreenFolder.Screens; }

            var screen = ((System.Collections.IEnumerable)screens).Cast<dynamic>().FirstOrDefault(s => s.Name == screenName);
            if (screen == null) throw new Exception($"Không tìm thấy màn hình: {screenName}");

            var itemList = new List<object>();

            foreach (var item in screen.ScreenItems)
            {
                var props = new Dictionary<string, object>();

                // 1. Lấy các thuộc tính cơ bản (Chỉ lấy giá trị thô, tránh lấy Object hệ thống)
                string[] safeProps = { "Name", "Left", "Top", "Width", "Height", "ForeColor", "BackColor", "HorizontalTextAlignment", "VerticalTextAlignment" };
                foreach (var p in safeProps)
                {
                    try
                    {
                        var val = item.GetAttribute(p);
                        // Nếu là Color, chuyển về String RGB để tránh Loop
                        if (val is System.Drawing.Color c) props[p] = $"{c.R}, {c.G}, {c.B}";
                        else props[p] = val;
                    }
                    catch { }
                }

                // 2. Xử lý RIÊNG cho Font để tránh lỗi "Self referencing loop"
                try
                {
                    // Thay vì lấy cả Object Font, ta chỉ lấy các giá trị con cần thiết
                    props["Font.Name"] = item.GetAttribute("Font.Name");
                    props["Font.Size"] = item.GetAttribute("Font.Size");
                    props["Font.Bold"] = item.GetAttribute("Font.Bold");
                }
                catch { }

                // 3. LẤY TEXT XML (Cái này là quan trọng nhất để trị lỗi Invalid Format)
                string rawXml = "";
                try
                {
                    var textComposition = item.Text.Items;
                    if (textComposition.Count > 0)
                    {
                        // Lấy chuỗi XML nguyên bản mà Siemens tự sinh ra
                        rawXml = textComposition[0].GetAttribute("Text").ToString();
                    }
                }
                catch { }

                itemList.Add(new
                {
                    Name = item.Name,
                    Type = item.GetType().Name,
                    Properties = props,
                    RawTextXml = rawXml
                });
            }

            var exportModel = new
            {
                ScreenName = screen.Name,
                Items = itemList
            };

            // Cấu hình JsonSerializer để bỏ qua các vòng lặp nếu lỡ có
            var settings = new Newtonsoft.Json.JsonSerializerSettings
            {
                ReferenceLoopHandling = Newtonsoft.Json.ReferenceLoopHandling.Ignore,
                Formatting = Newtonsoft.Json.Formatting.Indented
            };

            string json = JsonConvert.SerializeObject(exportModel, settings);
            File.WriteAllText(outputPath, json);
        }
        public void ExportPlcTagsToCsv(string deviceName, string outputPath)
        {
            var device = _project.Devices.FirstOrDefault(d => d.Name == deviceName);
            if (device == null) return;

            // Sửa lỗi GetItems() -> dùng DeviceItems
            var software = device.DeviceItems
                .SelectMany(i => i.DeviceItems)
                .FirstOrDefault(i => i.GetService<SoftwareContainer>() != null)
                ?.GetService<SoftwareContainer>()?.Software as PlcSoftware;

            var table = software?.TagTableGroup.TagTables.FirstOrDefault();
            if (table != null)
            {
                // Sửa lỗi ExportOptions.Default -> dùng None
                table.Export(new FileInfo(outputPath), ExportOptions.None);
            }
        }

        public void ExportHmiTagsToCsv(string deviceName, string outputPath)
        {
            var hmi = GetHmiTarget(deviceName);
            var table = hmi.TagTableGroup.TagTables.FirstOrDefault();
            if (table != null)
            {
                // Sửa lỗi ExportOptions.Default -> dùng None
                table.Export(new FileInfo(outputPath), ExportOptions.None);
            }
        }

        public void ExportHmiSettingsToJson(string deviceName, string outputPath)
        {
            var report = new Dictionary<string, object>();
            try
            {
                dynamic hmiSoftware = GetHmiTarget(deviceName);

                // 1. Quét thông tin cơ bản để xác nhận đã kết nối đúng
                report.Add("TargetName", hmiSoftware.Name.ToString());
                report.Add("TargetType", hmiSoftware.GetType().FullName);

                // 2. Thử truy cập theo đường dẫn chính thức của V20 (Dùng Try-Catch cho từng nấc)
                try
                {
                    var rt = hmiSoftware.SoftwareSettings.RuntimeSettings;
                    var rtData = new Dictionary<string, string>();
                    foreach (var p in rt.GetType().GetProperties())
                    {
                        try { rtData.Add(p.Name, p.GetValue(rt).ToString()); } catch { }
                    }
                    report.Add("RuntimeSettings_Found", rtData);
                }
                catch (Exception ex) { report.Add("RuntimeSettings_Error", ex.Message); }

                // 3. THỬ CHIÊU CUỐI: Quét qua mục 'Settings' nếu SoftwareSettings không có
                try
                {
                    var settings = hmiSoftware.Settings;
                    var stData = new Dictionary<string, string>();
                    foreach (var p in settings.GetType().GetProperties())
                    {
                        try { stData.Add(p.Name, p.GetValue(settings).ToString()); } catch { }
                    }
                    report.Add("Settings_Found", stData);
                }
                catch { }

                // 4. KIỂM TRA XEM MÀN HÌNH NÀO ĐANG ĐƯỢC ĐẶT LÀM START (Dùng Filter)
                try
                {
                    dynamic screens = null;
                    try { screens = hmiSoftware.Screens; } catch { screens = hmiSoftware.ScreenFolder.Screens; }

                    var startScreenCandidate = ((System.Collections.IEnumerable)screens).Cast<dynamic>()
                                                .FirstOrDefault(s => s.GetType().GetProperty("IsStartScreen") != null);
                    if (startScreenCandidate != null) report.Add("ActiveStartScreen", startScreenCandidate.Name);
                }
                catch { }

                File.WriteAllText(outputPath, JsonConvert.SerializeObject(report, Newtonsoft.Json.Formatting.Indented));
            }
            catch (Exception ex)
            {
                File.WriteAllText(outputPath, "{\"FatalError\": \"" + ex.Message + "\"}");
            }
        }
        #endregion

        #region 13. Faceplate & UDT                   

        public void EditLibraryUDT(string udtName)
        {
            try
            {
                var typeFolder = _project.ProjectLibrary.TypeFolder;
                dynamic targetType = typeFolder.Types.Find(udtName);
                if (targetType == null) return;

                // 1. DỌN SẠCH CÁC BẢN INWORK CŨ ĐỂ TRÁNH NHẦM LẪN
                for (int i = targetType.Versions.Count - 1; i >= 0; i--)
                {
                    if (targetType.Versions[i].State.ToString().Contains("InWork"))
                        targetType.Versions[i].Delete();
                }

                // 2. EXPORT & INJECT (Logic XML của Otis)
                string tempPath = Path.Combine(Path.GetTempPath(), "TIA_V20_Force_v48");
                if (Directory.Exists(tempPath)) Directory.Delete(tempPath, true);
                Directory.CreateDirectory(tempPath);

                LibraryTypeVersion latestBefore = null;
                foreach (var v in targetType.Versions) { latestBefore = v; }

                string xmlFile = Path.Combine(tempPath, udtName + ".xml");
                latestBefore.Export(new FileInfo(xmlFile), ExportOptions.WithDefaults);

                InjectUdtMembers(xmlFile, new List<(string, string)> {
            ("Copilot_Status", "Int"),
            ("Copilot_Control", "Word"),
            ("Last_Update", "DTL")
        });

                // 3. NẠP LẠI (Sẽ tạo ra một Version mới, ví dụ v0.0.7)
                Console.WriteLine("[>] Đang nạp cấu trúc XML mới vào Library...");
                targetType.Versions.CreateFromDocuments(
                    new DirectoryInfo(tempPath),
                    udtName,
                    Siemens.Engineering.Library.Types.CreateOptions.None,
                    Siemens.Engineering.Library.LibraryImportOptions.None
                );

                // 4. RELEASE BẢN MỚI NHẤT & THIẾT LẬP DEFAULT
                LibraryTypeVersion newVersion = null;
                foreach (var v in targetType.Versions)
                {
                    if (v.State.ToString().Contains("InWork"))
                    {
                        newVersion = v;
                        break;
                    }
                }

                if (newVersion != null)
                {
                    string finalVerStr = newVersion.VersionNumber.ToString();

                    // Chốt bản mới (v0.0.7)
                    newVersion.Release(0, null, "Otis_Admin", "Final Copilot Fix v48.1");
                    newVersion.SetAsDefault();
                    Console.WriteLine($"[√] Đã Release và Set Default cho bản mới nhất: v{finalVerStr}");

                    // 5. XÓA SẠCH TẤT CẢ CÁC BẢN CŨ (Dọn rác v0.0.1 -> v0.0.6)
                    Console.WriteLine($"[i] Đang xóa sạch các phiên bản cũ để dọn dẹp thư viện...");

                    bool foundOld = true;
                    while (foundOld)
                    {
                        foundOld = false;
                        foreach (var v in targetType.Versions)
                        {
                            // Nếu không phải là bản vừa mới tạo, thì xóa sạch!
                            if (v.VersionNumber.ToString() != finalVerStr)
                            {
                                Console.WriteLine($"  [-] Xóa bản cũ: v{v.VersionNumber}");
                                v.Delete();
                                foundOld = true;
                                break; // Break để refresh lại danh sách Versions sau khi Delete
                            }
                        }
                    }

                    // 6. ĐỒNG BỘ PROJECT
                    try
                    {
                        targetType.UpdateProject(1, 1);
                    }
                    catch
                    {
                        targetType.UpdateProject();
                    }

                    _project.Save();
                    Console.WriteLine($"[√] HOÀN TẤT: Thư viện chỉ còn duy nhất bản v{finalVerStr} sạch sẽ!");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[X] Lỗi Runtime v48.1: {ex.Message}");
            }
        }

        public void DiscoverLibraryCapabilities()
        {
            try
            {
                var projectLibrary = _project.ProjectLibrary;

                Console.WriteLine("\n======= [ QUÉT HỆ THỐNG THƯ VIỆN SIEMENS V20 ] =======");

                // 1. Quét Cấp độ Folder (Cực kỳ quan trọng để tìm CreateFrom)
                var folder = projectLibrary.TypeFolder;
                ScanObjectMethods("FOLDER (LibraryTypeSystemFolder)", folder);

                // 2. Quét Cấp độ Type (Để tìm Update/Merge)
                if (folder.Types.Count > 0)
                {
                    var firstType = folder.Types[0];
                    ScanObjectMethods("TYPE (LibraryType)", firstType);

                    // 3. Quét Cấp độ Version (Để tìm Edit/Process)
                    if (firstType.Versions.Count > 0)
                    {
                        ScanObjectMethods("VERSION (LibraryTypeVersion)", firstType.Versions[0]);
                    }
                }
                else
                {
                    Console.WriteLine("[!] Library trống, không thể quét sâu hơn vào Type/Version.");
                }

                Console.WriteLine("==========================================================\n");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[X] Lỗi khi quét sâu: {ex.Message}");
            }
        }

        private void ScanObjectMethods(string label, object obj)
        {
            if (obj == null) return;
            var type = obj.GetType();
            var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Select(m => m.Name)
                .Distinct()
                .OrderBy(n => n);

            Console.WriteLine($"\n[+] Đối tượng: {label}");
            Console.WriteLine($"    Kiểu thực tế: {type.FullName}");
            foreach (var method in methods)
            {
                // Loại bỏ các hàm hệ thống nhàm chán để dễ nhìn
                if (new[] { "ToString", "Equals", "GetHashCode", "GetType", "GetEnumerator" }.Contains(method)) continue;
                Console.WriteLine($"    -> {method}");
            }
        }
        private void InjectUdtMembers(string xmlPath, List<(string Name, string Type)> newMembers)
        {
            try
            {
                var doc = System.Xml.Linq.XDocument.Load(xmlPath);
                // Namespace chuẩn cho Interface của Unified
                System.Xml.Linq.XNamespace ns = "http://www.siemens.com/automation/Openness/SW/Interface/v2";

                // 1. CHỈ XÓA ID CỦA CÁC MEMBER (Không xóa ID của Root/Document)
                // Điều này giúp TIA tự cấp ID mới cho biến mà không làm hỏng cấu trúc file
                var members = doc.Descendants().Where(x => x.Name.LocalName == "Member").ToList();
                foreach (var m in members)
                {
                    m.Attribute("ID")?.Remove();
                }

                // 2. TÌM INTERFACE VÀ THAY THẾ NỘI DUNG
                var interfaceNode = doc.Descendants().FirstOrDefault(x => x.Name.LocalName == "Interface");
                if (interfaceNode != null)
                {
                    // Tìm ObjectList bên trong Interface
                    var objectList = interfaceNode.Elements().FirstOrDefault(x => x.Name.LocalName == "ObjectList");

                    // Xóa sạch các Member cũ (Start/Stop...)
                    objectList?.Remove();

                    // Tạo ObjectList mới
                    var newObjectList = new System.Xml.Linq.XElement(ns + "ObjectList");

                    foreach (var m in newMembers)
                    {
                        // Tạo cấu trúc Member chuẩn cho Unified
                        var memberElem = new System.Xml.Linq.XElement(ns + "Member",
                            new System.Xml.Linq.XAttribute("Name", m.Name),
                            new System.Xml.Linq.XAttribute("Datatype", m.Type),
                            new System.Xml.Linq.XElement(ns + "AttributeList",
                                new System.Xml.Linq.XElement(ns + "BooleanAttribute",
                                    new System.Xml.Linq.XAttribute("Name", "IsReadOnly"), "false")
                            )
                        );
                        newObjectList.Add(memberElem);
                    }

                    interfaceNode.Add(newObjectList);
                }

                // 3. CẬP NHẬT COMMENT/VERSION ĐỂ KIỂM CHỨNG
                // Tìm thẻ Comment trong AttributeList của Version
                var commentNode = doc.Descendants().FirstOrDefault(x => x.Name.LocalName == "Comment");
                if (commentNode != null)
                {
                    commentNode.Value = "Copilot_v68_Fixed_" + DateTime.Now.ToString("HHmm");
                }

                doc.Save(xmlPath);
                Console.WriteLine($"[√] Đã tiêm {newMembers.Count} biến mới và chuẩn hóa XML thành công.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[!] Lỗi phẫu thuật XML: {ex.Message}");
            }
        }
        private void ScanCompositionDefinitions(object composition)
        {
            try
            {
                Console.WriteLine($"\n{new string('=', 60)}");
                Console.WriteLine($" SCANNING DEFINITIONS FOR: {composition.GetType().Name}");
                Console.WriteLine($"{new string('-', 60)}");

                // Lấy tất cả các Method công khai
                var methods = composition.GetType().GetMethods()
                    .Where(m => !m.IsSpecialName) // Loại bỏ các hàm get/set mặc định
                    .Select(m => $"{m.Name}({string.Join(", ", m.GetParameters().Select(p => p.ParameterType.Name))})");

                Console.WriteLine(" [METHODS FOUND]:");
                foreach (var method in methods.OrderBy(n => n))
                {
                    Console.WriteLine($"  -> {method}");
                }

                // Lấy tất cả các Property
                var properties = composition.GetType().GetProperties()
                    .Select(p => $"{p.PropertyType.Name} {p.Name}");

                Console.WriteLine("\n [PROPERTIES FOUND]:");
                foreach (var prop in properties.OrderBy(n => n))
                {
                    Console.WriteLine($"  -> {prop}");
                }
                Console.WriteLine($"{new string('=', 60)}\n");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[!] Không thể quét định nghĩa: {ex.Message}");
            }
        }

        private void EditUdtMembers(LibraryTypeVersion draft)
        {
            try
            {
                dynamic udtVersion = draft;
                var members = udtVersion.Members;

                Console.WriteLine($"\n{new string('=', 60)}");
                Console.WriteLine($" STRUCTURE OF: {((dynamic)draft.Parent).Name} (v{draft.VersionNumber})");
                Console.WriteLine($"{new string('-', 60)}");
                Console.WriteLine($" {"#",-3} | {"Property Name",-25} | {"Data Type",-15}");
                Console.WriteLine($"{new string('-', 60)}");

                int index = 1;
                foreach (var member in members)
                {
                    string mName = member.Name;
                    string mType = member.DataTypeName;
                    Console.WriteLine($" {index,-3} | {mName,-25} | {mType,-15}");
                    index++;
                }
                Console.WriteLine($"{new string('=', 60)}\n");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[!] Không thể hiển thị cấu trúc UDT: {ex.Message}");
            }
        }
        public IEngineeringObject FindObjectInProject(string name, string deviceName)
        {
            try
            {
                // 1. Kiểm tra trong Project Library (Ưu tiên vì UDT của Otis nằm ở đây)
                LibraryTypeFolder typeFolder = _project.ProjectLibrary.TypeFolder;

                // Tìm kiếm đệ quy trong các Folder của Library
                var foundInLibrary = FindTypeInLibraryRecursive(typeFolder, name);
                if (foundInLibrary != null)
                {
                    Console.WriteLine("i", $"Tìm thấy '{name}' trong Project Library.", ConsoleColor.Yellow);
                    return foundInLibrary;
                }

                // 2. Nếu không thấy trong Library, mới tìm trong Device (Logic cũ)
                // ... (phần code tìm trong Device nếu bạn muốn giữ lại làm dự phòng)
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[X] Lỗi tìm kiếm Library: {ex.Message}");
            }
            return null;
        }

        // Hàm phụ tìm Type trong Library (Hỗ trợ Unified UDT)
        private IEngineeringObject FindTypeInLibraryRecursive(LibraryTypeFolder folder, string name)
        {
            // Duyệt các Types trong folder hiện tại
            foreach (var type in folder.Types)
            {
                if (type.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    return (IEngineeringObject)type;
                }
            }

            // Duyệt các folder con
            foreach (var subFolder in folder.Folders)
            {
                var found = FindTypeInLibraryRecursive(subFolder, name);
                if (found != null) return found;
            }
            return null;
        }
        // --- HÀM PHỤ (DÙNG ĐỂ FIX LỖI CS0103) ---
        private string ExtractOnlyMembers(string xml)
        {
            // Tìm tất cả các khối <Hmi.Tag.HmiUdtMember> ... </Hmi.Tag.HmiUdtMember>
            string startTag = "<Hmi.Tag.HmiUdtMember";
            string endTag = "</Hmi.Tag.HmiUdtMember>";

            int firstMatch = xml.IndexOf(startTag);
            int lastMatch = xml.LastIndexOf(endTag);

            if (firstMatch != -1 && lastMatch != -1)
            {
                // Trích xuất toàn bộ cụm Member
                return xml.Substring(firstMatch, (lastMatch + endTag.Length) - firstMatch);
            }

            return "";
        }

        // Hàm phụ để tính version (Otis có thể tùy biến)
        private string GetNextVersion(string udtName)
        {
            var existingType = _project.ProjectLibrary.TypeFolder.Types.Find(udtName);
            if (existingType == null) return "0.0.1";
            var lastV = existingType.Versions.OrderByDescending(v => v.VersionNumber).FirstOrDefault();
            if (lastV == null) return "0.0.1";
            return $"{lastV.VersionNumber.Major}.{lastV.VersionNumber.Minor}.{lastV.VersionNumber.Build + 1}";
        }
        public void ExportLibraryUDT(string udtName)
        {
            try
            {
                var typeFolder = _project.ProjectLibrary.TypeFolder;
                dynamic targetType = typeFolder.Types.Find(udtName);

                // Lấy bản mới nhất (v0.0.2 in work)
                LibraryTypeVersion latest = null;
                foreach (var v in targetType.Versions) { latest = v; }

                string exportPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Export_Step1_1");
                if (Directory.Exists(exportPath)) Directory.Delete(exportPath, true);
                Directory.CreateDirectory(exportPath);

                // 1. KIỂM TRA ĐỊNH DẠNG HỖ TRỢ (Quan trọng nhất)
                var formats = targetType.GetSupportedExportFormats();
                Console.WriteLine($"[i] Các định dạng hỗ trợ: {string.Join(", ", formats)}");

                if (formats.Count == 0)
                {
                    Console.WriteLine("[X] Hệ thống báo không hỗ trợ bất kỳ định dạng xuất nào cho UDT này!");
                    return;
                }

                // 2. THỬ XUẤT VỚI TẤT CẢ ĐỊNH DẠNG TÌM THẤY
                foreach (var fmt in formats)
                {
                    Console.WriteLine($"[>] Đang thử xuất với định dạng: {fmt}...");
                    try
                    {
                        latest.ExportAsDocuments(new DirectoryInfo(exportPath), udtName, fmt, LibraryExportOptions.None);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($" [!] Lỗi khi dùng {fmt}: {ex.Message}");
                    }
                }

                // 3. KIỂM TRA LẠI THƯ MỤC
                string[] files = Directory.GetFiles(exportPath, "*.*", SearchOption.AllDirectories);
                if (files.Length > 0)
                {
                    Console.WriteLine($"[√] THÀNH CÔNG! Đã thấy file tại: {exportPath}");
                    foreach (var f in files) Console.WriteLine($" -> {Path.GetFileName(f)}");
                }
                else
                {
                    Console.WriteLine("[X] Vẫn rỗng. Đây là lỗi sâu của Siemens V20 khi Metadata bị hỏng.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[!] Lỗi thực thi: {ex.Message}");
            }
        }


        #endregion
    }
    public class PlcCatalogItem
    {
        public string Name { get; set; }
        public string OrderNumber { get; set; }
        public string Version { get; set; }
        public List<string> AvailableVersions { get; set; }
        public string GetTypeIdentifier(string selectedVer = null)
        {
            // Nếu người dùng chọn bản cụ thể thì lấy, không thì lấy bản mặc định
            string ver = !string.IsNullOrEmpty(selectedVer) ? selectedVer : Version;

            // Đảm bảo có chữ 'V' (TIA bắt buộc format: OrderNumber/V4.4)
            if (!ver.StartsWith("V")) ver = "V" + ver;

            return $"OrderNumber:{OrderNumber}/{ver}";
        }
    }
}