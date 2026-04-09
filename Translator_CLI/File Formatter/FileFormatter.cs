using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;


namespace TIA_Copilot_CLI
{
    internal static class OutputPaths
    {
        private static string _cached = null;

        public static string GetGeneratedDir()
        {
            if (_cached != null) return _cached;

            DirectoryInfo dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);

            // Walk up until we land on the Translator_CLI project folder
            while (dir != null && !dir.Name.Equals("Translator_CLI", StringComparison.OrdinalIgnoreCase))
                dir = dir.Parent;

            string root = dir != null
                ? dir.FullName
                : AppDomain.CurrentDomain.BaseDirectory; // fallback — folder rename guard

            _cached = Path.Combine(root, "Generated_Files");
            Directory.CreateDirectory(_cached); // create on first use if missing
            return _cached;
        }
    }

    public static class SCLGenerator
    {
        public static void GenerateAndSave(BlockData data)
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                string blockType = string.IsNullOrEmpty(data.Type) ? "FUNCTION_BLOCK" : data.Type.ToUpper().Trim();
                if (blockType == "FB") blockType = "FUNCTION_BLOCK";
                if (blockType == "FC") blockType = "FUNCTION";
                if (blockType == "OB") blockType = "ORGANIZATION_BLOCK";

                // --- HEADER KHỐI CHÍNH ---
                if (blockType == "FUNCTION")
                {
                    sb.AppendLine($"FUNCTION \"{data.Name}\" : Void");
                }
                else if (blockType == "ORGANIZATION_BLOCK")
                {
                    sb.AppendLine($"ORGANIZATION_BLOCK \"{data.Name}\"");
                    sb.AppendLine("TITLE = \"Main Program Sweep (Cycle)\"");
                }
                else
                {
                    blockType = "FUNCTION_BLOCK";
                    sb.AppendLine($"FUNCTION_BLOCK \"{data.Name}\"");
                }

                sb.AppendLine("{ S7_Optimized_Access := 'TRUE' }");
                sb.AppendLine("VERSION : 0.1");

                // --- BỘ LỌC BIẾN (VARIABLE FILTER) CỰC NGHIÊM NGẶT ---
                if (data.Variables != null && data.Variables.Count > 0)
                {
                    var validVariables = data.Variables;

                    // Nếu là OB hoặc FC, TUYỆT ĐỐI CẤM dùng VAR (Static Memory)
                    if (blockType == "ORGANIZATION_BLOCK")
                    {
                        // OB chỉ được phép có TEMP và CONSTANT
                        validVariables = data.Variables.Where(v => v.Direction == "VAR_TEMP" || v.Direction == "VAR CONSTANT").ToList();
                    }
                    else if (blockType == "FUNCTION")
                    {
                        // FC không được phép có VAR
                        validVariables = data.Variables.Where(v => v.Direction != "VAR").ToList();
                    }

                    var order = new List<string> { "VAR_INPUT", "VAR_OUTPUT", "VAR_IN_OUT", "VAR", "VAR_TEMP", "VAR CONSTANT" };
                    var groupedVars = validVariables.GroupBy(v => string.IsNullOrEmpty(v.Direction) ? "VAR" : v.Direction.ToUpper().Trim()).OrderBy(g => order.IndexOf(g.Key) != -1 ? order.IndexOf(g.Key) : 99);

                    foreach (var group in groupedVars)
                    {
                        sb.AppendLine($"   {group.Key}");
                        foreach (var v in group)
                        {
                            string comment = string.IsNullOrWhiteSpace(v.Description) ? "" : $"   // {v.Description}";
                            sb.AppendLine($"      {v.Name} : {v.DataType};{comment}");
                        }
                        sb.AppendLine($"   END_VAR\n");
                    }
                }

                // --- BODY CODE ---
                sb.AppendLine("BEGIN");
                if (!string.IsNullOrWhiteSpace(data.BodyCode))
                {
                    var lines = data.BodyCode.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var line in lines) sb.AppendLine($"\t{line}");
                }
                sb.AppendLine($"END_{blockType}");

                // ==========================================================
                // THUẬT TOÁN QUÉT VÀ TỰ SINH DB BÊN TRONG FILE OB
                // ==========================================================
                if (blockType == "ORGANIZATION_BLOCK" && !string.IsNullOrWhiteSpace(data.BodyCode))
                {
                    // Regex tìm các chuỗi có dạng "Inst_FB_TênKhuôn_HậuTố"
                    // Ví dụ: "Inst_FB_MainConveyor_01" -> Match 1: Cả cụm, Match 2: FB_MainConveyor
                    string pattern = @"\""(Inst_(FB_[a-zA-Z0-9_]+)__([a-zA-Z0-9_]+))\""\s*\(";
                    MatchCollection matches = Regex.Matches(data.BodyCode, pattern);

                    HashSet<string> generatedDBs = new HashSet<string>();

                    foreach (Match match in matches)
                    {
                        string dbName = match.Groups[1].Value; // VD: Inst_FB_MainConveyor_01
                        string fbType = match.Groups[2].Value; // VD: FB_MainConveyor

                        // Dùng HashSet để tránh việc 1 DB bị gọi nhiều lần sinh ra nhiều khai báo trùng lặp
                        if (!generatedDBs.Contains(dbName))
                        {
                            generatedDBs.Add(dbName);
                            sb.AppendLine();
                            sb.AppendLine($"DATA_BLOCK \"{dbName}\"");
                            sb.AppendLine("{ S7_Optimized_Access := 'TRUE' }");
                            sb.AppendLine("VERSION : 0.1");
                            sb.AppendLine($"\"{fbType}\"");
                            sb.AppendLine("BEGIN");
                            sb.AppendLine("END_DATA_BLOCK");
                        }
                    }
                }

                // --- LƯU FILE SCL ---
                string safeName = string.IsNullOrWhiteSpace(data.Name) ? "AI_Generated_Block" : data.Name;
                string fileName = $"{safeName}.scl";
                string fullPath = Path.Combine(OutputPaths.GetGeneratedDir(), fileName);
                File.WriteAllText(fullPath, sb.ToString(), Encoding.UTF8);

                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine($"\n[SUCCESS] Exported successfully: {fileName}");
                Console.ResetColor();
                // Extract global tags if it's an OB
                if (data.Type.ToUpper() == "ORGANIZATION_BLOCK" && data.GlobalTags.Count > 0)
                {
                    ExtractAndSaveTagsToCSV(data);
                }
            }
            catch (Exception ex) { Console.WriteLine($"Error saving file: {ex.Message}"); }
        }

        public static void ExtractAndSaveTagsToCSV(BlockData data)
        {
            try
            {
                // Nếu AI không đẻ ra cái GlobalTag nào thì thoát luôn, không tạo file CSV
                if (data.GlobalTags == null || data.GlobalTags.Count == 0) return;

                StringBuilder csv = new StringBuilder();
                csv.AppendLine("Name,Path,Data Type,Logical Address,Comment,Hmi Visible,Hmi Accessible,Hmi Writeable,Typeobject ID,Version ID");

                int currentByte = 0;
                int currentBit = 0;

                foreach (var tag in data.GlobalTags)
                {
                    string name = tag.Name;
                    string dataType = tag.Type.ToUpper(); // Viết hoa để dễ IF/ELSE
                    string address = "";

                    if (dataType == "BOOL")
                    {
                        address = $"%M{currentByte}.{currentBit}";
                        currentBit++;
                        if (currentBit > 7)
                        {
                            currentBit = 0;
                            currentByte++;
                        }
                    }
                    else
                    {
                        if (currentBit > 0)
                        {
                            currentByte++;
                            currentBit = 0;
                        }

                        if (dataType == "REAL" || dataType == "DINT" || dataType == "DWORD" || dataType == "TIME")
                        {
                            if (currentByte % 2 != 0) currentByte++;
                            address = $"%MD{currentByte}";
                            currentByte += 4;
                        }
                        else if (dataType == "INT" || dataType == "WORD" || dataType == "UINT")
                        {
                            if (currentByte % 2 != 0) currentByte++;
                            address = $"%MW{currentByte}";
                            currentByte += 2;
                        }
                        else if (dataType == "BYTE" || dataType == "SINT" || dataType == "USINT")
                        {
                            address = $"%MB{currentByte}";
                            currentByte += 1;
                        }
                    }

                    // Ép Data Type cho viết hoa chữ cái đầu cho chuẩn TIA (VD: Bool, Real, Int)
                    string formattedDataType = char.ToUpper(dataType[0]) + dataType.Substring(1).ToLower();

                    string finalComment = string.IsNullOrWhiteSpace(tag.Comment)
                                          ? "AI Generated"
                                          : $"[{tag.Comment}]";

                    csv.AppendLine($"{name},Default tag table,{formattedDataType},{address},{finalComment},True,True,True,,");
                }

                // ---> SỬ DỤNG data.Name ĐỂ ĐẶT TÊN FILE
                string safeName = string.IsNullOrWhiteSpace(data.Name) ? "AI_Generated" : data.Name;
                string fileName = $"{safeName}_Tags.csv";
                string fullPath = Path.Combine(OutputPaths.GetGeneratedDir(), fileName);
                File.WriteAllText(fullPath, csv.ToString(), Encoding.UTF8);

                Console.ForegroundColor = ConsoleColor.Magenta;
                Console.WriteLine($"\n[MEMORY ALLOCATOR] Đã cấp phát địa chỉ Siemens cho {data.GlobalTags.Count} Global Tags.");
                Console.WriteLine($"[SUCCESS] Đã xuất file I/O List CSV: {fileName}");
                Console.ResetColor();
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"\n[LỖI] khi xuất file CSV Tags: {ex.Message}");
                Console.ResetColor();
            }
        }
    }


    public static class DataNormalizer
    {
        public static BlockData Normalize(JObject root)
        {
            var data = new BlockData();

            // ... (Khu vực xử lý chuẩn JSON cũ) ...
            if (root.ContainsKey("iec_61131_3_code"))
            {
                var core = root["iec_61131_3_code"];
                data.Name = core["name"]?.ToString() ?? "Unknown";
                data.Type = core["pou_type"]?.ToString() ?? "FUNCTION_BLOCK";
                data.Description = "Generated by AI";

                if (core["body"] != null && core["body"]["code"] != null)
                {
                    if (core["body"]["code"] is JArray arr)
                        data.BodyCode = string.Join("\n", arr.Select(c => c.ToString()));
                    else
                        data.BodyCode = core["body"]["code"].ToString();
                }

                if (core["variables"] != null)
                {
                    foreach (var v in core["variables"])
                    {
                        data.Variables.Add(new VariableInfo
                        {
                            Name = v["name"]?.ToString(),
                            DataType = v["data_type"]?.ToString(),
                            Direction = v["type"]?.ToString(),
                            Description = v["description"]?.ToString()
                        });
                    }
                }
            }
            // ... (Khu vực xử lý chuẩn JSON mới) ...
            else if (root.ContainsKey("block_info"))
            {
                var info = root["block_info"];
                data.Name = info["name"]?.ToString(); data.Type = info["type"]?.ToString();
                data.Description = info["description"]?.ToString(); data.BodyCode = root["body_code"]?.ToString();

                if (root["interface"] != null)
                {
                    foreach (var v in root["interface"])
                    {
                        data.Variables.Add(new VariableInfo
                        {
                            Name = v["name"]?.ToString(),
                            DataType = v["type"]?.ToString(),
                            Direction = v["direction"]?.ToString(),
                            Description = v["description"]?.ToString()
                        });
                    }
                }
            }
            if (root.ContainsKey("global_tags") && root["global_tags"] != null)
            {
                foreach (var tag in root["global_tags"])
                {
                    data.GlobalTags.Add(new GlobalTag
                    {
                        Name = tag["name"]?.ToString() ?? "",
                        Type = tag["type"]?.ToString() ?? "BOOL",
                        Comment = tag["comment"]?.ToString() ?? "Auto-generated"
                    });
                }
            }

            return data;
        }
    }

    public class VariableInfo
    {
        public string Name { get; set; }
        public string DataType { get; set; }
        public string Direction { get; set; }
        public string Description { get; set; }
    }

    public class GlobalTag
    {
        public string Name { get; set; }
        public string Type { get; set; }
        public string Comment { get; set; }
    }

    public class BlockData
    {
        public string Name { get; set; }
        public string Type { get; set; }
        public string Description { get; set; }
        public string BodyCode { get; set; }
        public List<VariableInfo> Variables { get; set; } = new List<VariableInfo>();

        public List<GlobalTag> GlobalTags { get; set; } = new List<GlobalTag>();
    }
}

// ==========================================================================
// HMI SECTION — Models, Normalizer, and Physical Assembler
// Handles AI logical JSON → WinCC Unified physical JSON (for tia draw)
// ==========================================================================
namespace TIA_Copilot_CLI
{
    // -----------------------------------------------------------------------
    // DATA MODELS
    // -----------------------------------------------------------------------
    public class TagWrite
    {
        public string Tag { get; set; }
        public int Value { get; set; }
    }

    public class HmiItemData
    {
        public string Name { get; set; }
        public string Type { get; set; }         // Tank, Valve, Button, IOField, etc.
        public string SubType { get; set; }      // ControlValve, Motor2, PipeVertical, etc.
        public string BindTag { get; set; }      // Primary tag binding
        public Dictionary<string, object> Properties { get; set; } = new Dictionary<string, object>();
        public List<string> Behaviors { get; set; } = new List<string>(); // fill_level, color_on_status
        public string Hint { get; set; }         // Layout hint for C# assembler
        public string Label { get; set; }        // Button label text
        public string NavigateTo { get; set; }   // Navigation button target screen
        public TagWrite KeydownWrite { get; set; }
        public TagWrite KeyupWrite { get; set; }
        public string Format { get; set; }       // IOField / Clock format string
        public double? MinValue { get; set; }    // Bar / Gauge min
        public double? MaxValue { get; set; }    // Bar / Gauge max
        public string ClockMode { get; set; }    // LocalTime / SystemTime
        public string Tooltip { get; set; }      // TouchArea tooltip
        public string TrendTag { get; set; }     // TrendControl tag name
        public bool ShowRuler { get; set; }      // TrendControl ruler
        public int? ParameterSetId { get; set; } // DetailedParameterControl
        public string Url { get; set; }          // MediaControl / WebControl
        public string ScreenName { get; set; }   // ScreenWindow sub-screen name
        public string BindTagWrite { get; set; } // CheckBox / RadioButton write tag
        public string BackColor { get; set; } // HmiToggleSwitch default state color (R, G, B)
        public string AlternateBackColor { get; set; } // HmiToggleSwitch alternate/active state color (R, G, B)
    }

    public class HmiScreenData
    {
        public string ScreenName { get; set; }
        public int Width { get; set; } = 1024;
        public int Height { get; set; } = 600;
        public List<HmiItemData> Items { get; set; } = new List<HmiItemData>();
        public List<GlobalTag> GlobalTags { get; set; } = new List<GlobalTag>();
    }

    // -----------------------------------------------------------------------
    // NORMALIZER: AI logical JSON → HmiScreenData
    // -----------------------------------------------------------------------
    public static class HmiDataNormalizer
    {
        public static HmiScreenData Normalize(JObject root)
        {
            var data = new HmiScreenData();

            var screenInfo = root["screen_info"];
            if (screenInfo != null)
            {
                data.ScreenName = screenInfo["name"]?.ToString() ?? "AI_Generated_Screen";
                data.Width = screenInfo["width"]?.ToObject<int>() ?? 1024;
                data.Height = screenInfo["height"]?.ToObject<int>() ?? 600;
            }

            var items = root["items"] as JArray;
            if (items != null)
            {
                foreach (JObject item in items)
                {
                    // Skip schema comment-only entries
                    if (!item.ContainsKey("name") || !item.ContainsKey("type")) continue;

                    var hmiItem = new HmiItemData
                    {
                        Name = item["name"]?.ToString(),
                        Type = item["type"]?.ToString(),
                        SubType = item["subtype"]?.ToString(),
                        BindTag = item["bind_tag"]?.ToString(),
                        Hint = item["hint"]?.ToString(),
                        Label = item["label"]?.ToString(),
                        NavigateTo = item["navigate_to"]?.ToString(),
                        Format = item["format"]?.ToString(),
                        MinValue = item["min_value"]?.ToObject<double?>(),
                        MaxValue = item["max_value"]?.ToObject<double?>(),
                        ClockMode = item["clock_mode"]?.ToString(),
                        Tooltip = item["tooltip"]?.ToString(),
                        TrendTag = item["trend_tag"]?.ToString(),
                        ShowRuler = item["show_ruler"]?.ToObject<bool>() ?? false,
                        ParameterSetId = item["parameter_set_id"]?.ToObject<int?>(),
                        Url = item["url"]?.ToString(),
                        ScreenName = item["screen_name"]?.ToString(),
                        BindTagWrite = item["bind_tag_write"]?.ToString(),
                    };

                    // Parse behaviors array
                    if (item["behaviors"] is JArray behaviors)
                        foreach (var b in behaviors)
                            hmiItem.Behaviors.Add(b.ToString());

                    // Parse button write actions
                    if (item["keydown_write"] != null)
                        hmiItem.KeydownWrite = new TagWrite
                        {
                            Tag = item["keydown_write"]["tag"]?.ToString(),
                            Value = item["keydown_write"]["value"]?.ToObject<int>() ?? 1
                        };

                    if (item["keyup_write"] != null)
                        hmiItem.KeyupWrite = new TagWrite
                        {
                            Tag = item["keyup_write"]["tag"]?.ToString(),
                            Value = item["keyup_write"]["value"]?.ToObject<int>() ?? 0
                        };

                    data.Items.Add(hmiItem);
                }
            }

            // Parse global_tags (same format as SCL)
            if (root["global_tags"] is JArray globalTags)
            {
                foreach (var tag in globalTags)
                {
                    data.GlobalTags.Add(new GlobalTag
                    {
                        Name = tag["name"]?.ToString() ?? "",
                        Type = tag["type"]?.ToString() ?? "BOOL",
                        Comment = tag["comment"]?.ToString() ?? "HMI Tag"
                    });
                }
            }

            return data;
        }
    }

    // -----------------------------------------------------------------------
    // GENERATOR: HmiScreenData → Physical WinCC Unified JSON
    // Output format matches scada_multi_screen_test.json (consumed by tia draw)
    // -----------------------------------------------------------------------
    public static class HmiGenerator
    {
        // --- LAYOUT ENGINE ---
        // Zone model: Process area (center), Controls (left sidebar), Status bar (top)
        // C# stamps coordinates based on type and a running slot counter per zone.
        // [TO BE REFINED] — adjust zone origins and sizes to match your screen layout.

        // --- CONNECTION COUNTER ---
        // Increments by 1 each time a new HMI Tags CSV is exported.
        // Produces connection names: HMI_PLC_Conn_1, HMI_PLC_Conn_2, ...
        private static int _connectionCounter = 0;

        // 
        // private static readonly int SIDEBAR_X = 30;
        // private static readonly int SIDEBAR_Y_START = 180;
        // private static readonly int SIDEBAR_BTN_W = 120;
        // private static readonly int SIDEBAR_BTN_H = 40;
        // private static readonly int SIDEBAR_GAP = 10;

        private static readonly int PROCESS_X = 300;
        private static readonly int PROCESS_Y = 100;

        private static readonly int INDICATOR_X = 590;
        private static readonly int INDICATOR_Y_START = 160;

        private static readonly int DATACTL_X = 10;
        private static readonly int DATACTL_Y_START = 10;
        private static readonly int DATACTL_W = 500;
        private static readonly int DATACTL_H = 280;
        private static readonly int DATACTL_GAP = 10;
        private static readonly int FALLBACK_X = 20;
        private static readonly int FALLBACK_Y = 700;
        private static readonly int DEFAULT_W = 120;
        private static readonly int DEFAULT_H = 40;
        private static readonly int V_SPACING = 10;

        public static void GenerateAndSave(HmiScreenData data)
        {
            try
            {
                // Slot counters per zone — increment as objects are placed
                int buttonSlot = 0;
                int indicatorSlot = 0;
                int dataCtlSlot = 0;

                var itemsJson = new JArray();

                foreach (var item in data.Items)
                {
                    JObject physicalItem = BuildPhysicalItem(
                        item,
                        ref buttonSlot,
                        ref indicatorSlot,
                        ref dataCtlSlot
                    );

                    if (physicalItem != null)
                        itemsJson.Add(physicalItem);
                }

                // Wrap in single-screen project structure (matches ScadaProjectModel)
                var projectJson = new JObject
                {
                    ["ProjectName"] = "AI_HMI_Project",
                    ["DeviceName"] = "PC-System_1",
                    ["StartScreenName"] = data.ScreenName,
                    ["Screens"] = new JArray
                    {
                        new JObject
                        {
                            ["ScreenName"] = data.ScreenName,
                            ["Width"]      = data.Width,
                            ["Height"]     = data.Height,
                            ["Items"]      = itemsJson
                        }
                    }
                };

                // Save physical JSON
                string safeName = string.IsNullOrWhiteSpace(data.ScreenName) ? "AI_Screen" : data.ScreenName;
                string fileName = $"{safeName}.json";
                string fullPath = Path.Combine(OutputPaths.GetGeneratedDir(), fileName);

                File.WriteAllText(fullPath, projectJson.ToString(Newtonsoft.Json.Formatting.Indented), Encoding.UTF8);

                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine($"\n[SUCCESS] HMI screen exported: {fileName}");
                Console.ResetColor();
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine($" Run 'tia draw \"{fullPath}\"' to push to TIA Portal.");
                Console.ResetColor();

                // Also export HMI tags CSV if any global_tags returned
                if (data.GlobalTags != null && data.GlobalTags.Count > 0)
                    ExportHmiTagsCsv(data);
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"\n[HMI GENERATOR ERROR]: {ex.Message}");
                Console.ResetColor();
            }
        }

        // private static JObject BuildPhysicalItem(
        //     HmiItemData item,
        //     ref int buttonSlot,
        //     ref int indicatorSlot,
        //     ref int dataCtlSlot)
        // {
        //     var props = new JObject();
        //     string type = item.Type ?? "";

        //     switch (type)
        //     {
        //         // --- LIBRARY OBJECTS ---
        //         case "Tank":
        //             props["LibraryPath"] = "IndustryGraphicLibrary/Tanks";
        //             props["SubType"] = item.SubType ?? "Tank";
        //             props["Left"] = PROCESS_X + 160;
        //             props["Top"] = PROCESS_Y + 35;
        //             props["Width"] = 160;
        //             props["Height"] = 340;
        //             props["LevelTag"] = item.BindTag ?? "";
        //             props["DisplayFillLevel"] = item.Behaviors.Contains("fill_level");
        //             if (item.Behaviors.Contains("fill_level"))
        //                 props["FillLevelColor"] = "255, 161, 0";
        //             break;

        //         case "Valve":
        //             props["LibraryPath"] = "IndustryGraphicLibrary/Valves";
        //             props["SubType"] = item.SubType ?? "ControlValve";
        //             props["Left"] = PROCESS_X + 40;
        //             props["Top"] = PROCESS_Y - 25;
        //             props["Width"] = 110;
        //             props["Height"] = 90;
        //             props["StatusTag"] = item.BindTag ?? "";
        //             AddColorScript(props, item);
        //             break;

        //         case "Motor":
        //             props["LibraryPath"] = "IndustryGraphicLibrary/Motors";
        //             props["SubType"] = item.SubType ?? "Motor2";
        //             props["Left"] = PROCESS_X - 70;
        //             props["Top"] = PROCESS_Y + 335;
        //             props["Width"] = 145;
        //             props["Height"] = 105;
        //             props["StatusTag"] = item.BindTag ?? "";
        //             AddColorScript(props, item);
        //             break;

        //         case "Pipe":
        //             props["LibraryPath"] = "IndustryGraphicLibrary/Pipes";
        //             props["SubType"] = item.SubType ?? "PipeHorizontal";
        //             props["Left"] = PROCESS_X - 45;
        //             props["Top"] = PROCESS_Y;
        //             props["Width"] = (item.SubType == "PipeVertical") ? 15 : 245;
        //             props["Height"] = (item.SubType == "PipeVertical") ? 315 : 15;
        //             props["BasicColor"] = "238, 238, 238";
        //             props["StatusTag"] = item.BindTag ?? "";
        //             break;

        //         // --- PRIMITIVE SHAPES ---
        //         case "Rectangle":
        //             props["Left"] = INDICATOR_X;
        //             props["Top"] = INDICATOR_Y_START + (indicatorSlot * 35);
        //             props["Width"] = 25;
        //             props["Height"] = 25;
        //             props["StatusTag"] = item.BindTag ?? "";
        //             AddColorScript(props, item);
        //             indicatorSlot++;
        //             break;

        //         case "Circle":
        //             props["CenterX"] = INDICATOR_X + 12;
        //             props["CenterY"] = INDICATOR_Y_START + (indicatorSlot * 35) + 12;
        //             props["Radius"] = 12;
        //             props["Tag"] = item.BindTag ?? "";
        //             AddColorScript(props, item);
        //             indicatorSlot++;
        //             break;

        //         case "CircularArc":
        //             props["CenterX"] = INDICATOR_X + 12;
        //             props["CenterY"] = INDICATOR_Y_START + (indicatorSlot * 35) + 12;
        //             props["Radius"] = 12;
        //             props["AngleStart"] = 270;
        //             props["AngleRange"] = 90;
        //             props["Tag"] = item.BindTag ?? "";
        //             indicatorSlot++;
        //             break;

        //         case "CircleSegment":
        //             props["CenterX"] = INDICATOR_X + 12;
        //             props["CenterY"] = INDICATOR_Y_START + (indicatorSlot * 35) + 12;
        //             props["Radius"] = 12;
        //             props["AngleStart"] = 270;
        //             props["AngleRange"] = 90;
        //             props["Tag"] = item.BindTag ?? "";
        //             indicatorSlot++;
        //             break;

        //         // --- BUTTONS ---
        //         case "Button":
        //             int btnY = SIDEBAR_Y_START + buttonSlot * (SIDEBAR_BTN_H + SIDEBAR_GAP);
        //             props["Left"] = SIDEBAR_X;
        //             props["Top"] = btnY;
        //             props["Width"] = SIDEBAR_BTN_W;
        //             props["Height"] = SIDEBAR_BTN_H;
        //             props["Text"] = item.Label ?? item.Name;

        //             var scripts = new JObject();
        //             if (!string.IsNullOrEmpty(item.NavigateTo))
        //             {
        //                 // Navigation button
        //                 scripts["KeyUp"] = $"HMIRuntime.UI.SysFct.ChangeScreen('{item.NavigateTo}', null);";
        //             }
        //             else
        //             {
        //                 // Momentary write button
        //                 if (item.KeydownWrite != null)
        //                     scripts["KeyDown"] = $"Tags(\"{item.KeydownWrite.Tag}\").Write({item.KeydownWrite.Value});";
        //                 if (item.KeyupWrite != null)
        //                     scripts["KeyUp"] = $"Tags(\"{item.KeyupWrite.Tag}\").Write({item.KeyupWrite.Value});";
        //             }
        //             props["Scripts"] = scripts;
        //             buttonSlot++;
        //             break;

        //         // --- I/O CONTROLS ---
        //         case "IOField":
        //             props["Left"] = SIDEBAR_X;
        //             props["Top"] = 50;
        //             props["Width"] = 120;
        //             props["Height"] = 40;
        //             props["Format"] = item.Format ?? "{0}";
        //             props["StatusTag"] = item.BindTag ?? "";
        //             break;

        //         case "Bar":
        //             props["Left"] = PROCESS_X - 50;
        //             props["Top"] = PROCESS_Y;
        //             props["Width"] = 50;
        //             props["Height"] = 200;
        //             props["Tag"] = item.BindTag ?? "";
        //             props["MinValue"] = item.MinValue ?? 0;
        //             props["MaxValue"] = item.MaxValue ?? 100;
        //             break;

        //         case "Gauge":
        //             props["Left"] = PROCESS_X + 150;
        //             props["Top"] = PROCESS_Y - 50;
        //             props["Width"] = 150;
        //             props["Height"] = 150;
        //             props["Tag"] = item.BindTag ?? "";
        //             props["MinValue"] = item.MinValue ?? 0;
        //             props["MaxValue"] = item.MaxValue ?? 100;
        //             break;

        //         case "Clock":
        //             props["Left"] = SIDEBAR_X;
        //             props["Top"] = 20;
        //             props["Width"] = 200;
        //             props["Height"] = 50;
        //             props["Format"] = item.Format ?? "{P, hh:mm:ss}";
        //             props["ClockMode"] = item.ClockMode ?? "LocalTime";
        //             break;

        //         case "TouchArea":
        //             // Default: overlay over the process center — hint drives final tuning
        //             props["Left"] = PROCESS_X + 160;
        //             props["Top"] = PROCESS_Y + 35;
        //             props["Width"] = 160;
        //             props["Height"] = 340;
        //             props["ToolTipText"] = item.Tooltip ?? "";
        //             props["Tag"] = item.BindTag ?? "";
        //             break;

        //         case "CheckBoxGroup":
        //             props["Left"] = SIDEBAR_X;
        //             props["Top"] = SIDEBAR_Y_START + buttonSlot * (SIDEBAR_BTN_H + SIDEBAR_GAP);
        //             props["Width"] = SIDEBAR_BTN_W;
        //             props["Height"] = SIDEBAR_BTN_H;
        //             props["Text"] = item.Label ?? item.Name;
        //             props["Tag"] = item.BindTag ?? "";
        //             buttonSlot++;
        //             break;

        //         case "RadioButtonGroup":
        //             props["Left"] = SIDEBAR_X;
        //             props["Top"] = SIDEBAR_Y_START + buttonSlot * (SIDEBAR_BTN_H + SIDEBAR_GAP);
        //             props["Width"] = SIDEBAR_BTN_W;
        //             props["Height"] = 80;
        //             props["Text"] = item.Label ?? item.Name;
        //             props["Tag"] = item.BindTag ?? "";
        //             buttonSlot += 2; // taller slot
        //             break;

        //         case "HmiToggleSwitch":
        //             props["Left"] = SIDEBAR_X;
        //             props["Top"] = SIDEBAR_Y_START + buttonSlot * (SIDEBAR_BTN_H + SIDEBAR_GAP);
        //             props["Width"] = 80;
        //             props["Height"] = 40;
        //             // BackColor: use AI-provided value or sensible industrial default (light gray-blue)
        //             props["BackColor"] = !string.IsNullOrEmpty(item.BackColor)
        //                                             ? item.BackColor
        //                                             : "242, 244, 255";
        //             // AlternateBackColor: the "ON" state color — use AI-provided value or green
        //             props["AlternateBackColor"] = !string.IsNullOrEmpty(item.AlternateBackColor)
        //                                             ? item.AlternateBackColor
        //                                             : "0, 200, 80";
        //             // TagColor links the switch state to the tag — always driven by bind_tag
        //             props["TagColor"] = item.BindTag ?? "";
        //             // Events nested object — script always generated from bind_tag, never written by AI
        //             props["Events"] = new JObject
        //             {
        //                 ["OnStateChanged"] = !string.IsNullOrEmpty(item.BindTag)
        //                     ? $"Tags(\"{item.BindTag}\").Write(item.IsAlternateState);"
        //                     : ""
        //             };
        //             buttonSlot++;
        //             break;

        //         // --- DATA CONTROLS ---
        //         case "TrendControl":
        //             props["Left"] = DATACTL_X + (dataCtlSlot > 0 ? DATACTL_W + DATACTL_GAP : 0);
        //             props["Top"] = DATACTL_Y_START;
        //             props["Width"] = DATACTL_W;
        //             props["Height"] = DATACTL_H;
        //             props["TrendName"] = item.TrendTag ?? item.BindTag ?? "";
        //             props["ShowRuler"] = item.ShowRuler;
        //             dataCtlSlot++;
        //             break;

        //         case "AlarmControl":
        //             props["Left"] = DATACTL_X + (dataCtlSlot > 0 ? DATACTL_W + DATACTL_GAP : 0);
        //             props["Top"] = DATACTL_Y_START;
        //             props["Width"] = DATACTL_W;
        //             props["Height"] = DATACTL_H;
        //             dataCtlSlot++;
        //             break;

        //         case "FunctionTrendControl":
        //             props["Left"] = DATACTL_X;
        //             props["Top"] = DATACTL_Y_START + DATACTL_H + DATACTL_GAP;
        //             props["Width"] = DATACTL_W - 100;
        //             props["Height"] = 250;
        //             break;

        //         case "SystemDiagnosisControl":
        //             props["Left"] = DATACTL_X;
        //             props["Top"] = DATACTL_Y_START;
        //             props["Width"] = DATACTL_W;
        //             props["Height"] = DATACTL_H;
        //             break;

        //         case "DetailedParameterControl":
        //             props["Left"] = DATACTL_X + DATACTL_W + DATACTL_GAP;
        //             props["Top"] = DATACTL_Y_START + DATACTL_H + DATACTL_GAP;
        //             props["Width"] = DATACTL_W;
        //             props["Height"] = 250;
        //             props["ParameterSetID"] = item.ParameterSetId ?? 1;
        //             break;

        //         // --- MEDIA & WEB ---
        //         case "MediaControl":
        //             props["Left"] = DATACTL_X;
        //             props["Top"] = DATACTL_Y_START;
        //             props["Width"] = 300;
        //             props["Height"] = 200;
        //             props["Url"] = item.Url ?? "";
        //             break;

        //         case "WebControl":
        //             props["Left"] = DATACTL_X + 320;
        //             props["Top"] = DATACTL_Y_START;
        //             props["Width"] = 400;
        //             props["Height"] = 300;
        //             props["Url"] = item.Url ?? "";
        //             break;

        //         // --- CONTAINER ---
        //         case "ScreenWindow":
        //             props["Left"] = DATACTL_X;
        //             props["Top"] = DATACTL_Y_START + DATACTL_H + DATACTL_GAP;
        //             props["Width"] = 300;
        //             props["Height"] = 200;
        //             props["ScreenName"] = item.ScreenName ?? "";
        //             break;

        //         default:
        //             // [TO BE IMPLEMENTED] — unknown type, skip with warning
        //             Console.ForegroundColor = ConsoleColor.Yellow;
        //             Console.WriteLine($"[HMI WARNING] Unknown item type '{type}' for '{item.Name}' — skipped.");
        //             Console.ResetColor();
        //             return null;
        //     }

        //     return new JObject
        //     {
        //         ["Name"] = item.Name,
        //         ["Type"] = type,
        //         ["Properties"] = props
        //     };
        // }

private static JObject BuildPhysicalItem(
    HmiItemData item,
    ref int buttonSlot,
    ref int indicatorSlot,
    ref int dataCtlSlot)
{
    var props = new JObject();
    string type = item.Type ?? "";
    string itemName = item.Name ?? "";

    // 1. TỰ ĐỘNG PHÂN CỤM (CLUSTER LOGIC)
    int clusterOffsetX = 0;
    if (itemName.Contains("M1")) clusterOffsetX = 0;
    else if (itemName.Contains("M2")) clusterOffsetX = 250;
    else if (itemName.Contains("M3")) clusterOffsetX = 500;
    else if (itemName.Contains("M4")) clusterOffsetX = 750;

    int baseLeft = FALLBACK_X + clusterOffsetX;

    // 2. LOGIC VỊ TRÍ NỘI BỘ (INTERNAL Y)
    int internalY = 0;
    if (itemName.ToUpper().Contains("Background")) internalY = 0;
    else if (itemName.ToUpper().Contains("Display") || type == "Clock") internalY = 20;
    else if (itemName.ToUpper().Contains("START")) internalY = 80;
    else if (itemName.ToUpper().Contains("STOP")) internalY = 130;
    else if (itemName.ToUpper().Contains("RESET")) internalY = 180;
    else if (itemName.ToUpper().Contains("Mode") || type.Contains("Switch") || type.Contains("Group")) internalY = 230;

    switch (type)
    {
        // --- LIBRARY OBJECTS ---
        case "Tank":
            props["LibraryPath"] = "IndustryGraphicLibrary/Tanks";
            props["SubType"] = item.SubType ?? "Tank";
            props["Left"] = baseLeft + 20;
            props["Top"] = FALLBACK_Y + 50;
            props["Width"] = 160u; props["Height"] = 340u;
            props["LevelTag"] = item.BindTag ?? "";
            break;

        case "Valve":
        case "Motor":
            props["LibraryPath"] = (type == "Valve") ? "IndustryGraphicLibrary/Valves" : "IndustryGraphicLibrary/Motors";
            props["SubType"] = item.SubType ?? (type == "Valve" ? "ControlValve" : "Motor2");
            props["Left"] = baseLeft + 45;
            props["Top"] = FALLBACK_Y + 60;
            props["Width"] = 110u; props["Height"] = 90u;
            props["StatusTag"] = item.BindTag ?? "";
            AddColorScript(props, item);
            break;

        case "Pipe":
            props["LibraryPath"] = "IndustryGraphicLibrary/Pipes";
            props["SubType"] = item.SubType ?? "PipeHorizontal";
            props["Left"] = baseLeft;
            props["Top"] = FALLBACK_Y + 100;
            props["Width"] = (item.SubType == "PipeVertical") ? 15u : 260u;
            props["Height"] = (item.SubType == "PipeVertical") ? 315u : 15u;
            props["BasicColor"] = "238, 238, 238";
            break;

        // --- PRIMITIVE SHAPES ---
        case "Rectangle":
            props["Left"] = baseLeft;
            props["Top"] = FALLBACK_Y;
            props["Width"] = 240u; // Tăng nhẹ Width để bao quát linh kiện
            props["Height"] = 350u; // Tăng Height để không bị lòi Switch
            break;

        case "Circle":
        case "CircularArc":
        case "CircleSegment":
            props["CenterX"] = baseLeft + 180;
            props["CenterY"] = FALLBACK_Y + (itemName.ToLower().Contains("error") || itemName.ToLower().Contains("fault") ? 140 : 100);
            props["Radius"] = 12u;
            if (type != "Circle") { props["AngleStart"] = 270; props["AngleRange"] = 90; }
            props["Tag"] = item.BindTag ?? "";
            AddColorScript(props, item);
            break;

        // --- BUTTONS ---
        case "Button":
            props["Left"] = baseLeft + 15;
            props["Top"] = FALLBACK_Y + internalY;
            props["Width"] = 100u; props["Height"] = 40u;
            props["Text"] = item.Label ?? itemName;
            var scripts = new JObject();
            if (item.KeydownWrite != null) scripts["KeyDown"] = $"Tags(\"{item.KeydownWrite.Tag}\").Write({item.KeydownWrite.Value});";
            if (item.KeyupWrite != null) scripts["KeyUp"] = $"Tags(\"{item.KeyupWrite.Tag}\").Write({item.KeyupWrite.Value});";
            props["Scripts"] = scripts;
            break;

        // --- I/O CONTROLS ---
        case "IOField":
            props["Left"] = baseLeft + 15;
            props["Top"] = FALLBACK_Y + internalY;
            props["Width"] = 160u; props["Height"] = 35u;
            props["Format"] = item.Format ?? "{0}";
            props["StatusTag"] = item.BindTag ?? "";
            break;

        case "Bar":
        case "Gauge":
            props["Left"] = baseLeft + 40;
            props["Top"] = FALLBACK_Y + 50;
            props["Width"] = (type == "Bar" ? 50u : 120u);
            props["Height"] = (type == "Bar" ? 180u : 120u);
            props["Tag"] = item.BindTag ?? "";
            break;

        case "Clock":
            props["Left"] = baseLeft + 15;
            props["Top"] = FALLBACK_Y + internalY;
            props["Width"] = 160u; props["Height"] = 35u;
            break;

        case "TouchArea":
            props["Left"] = baseLeft; props["Top"] = FALLBACK_Y;
            props["Width"] = 240u; props["Height"] = 350u;
            break;

        case "CheckBoxGroup":
        case "RadioButtonGroup":
        case "HmiToggleSwitch":
            props["Left"] = baseLeft + 15;
            props["Top"] = FALLBACK_Y + internalY;
            props["Width"] = (type.Contains("Switch") ? 80u : 150u);
            props["Height"] = (type.Contains("Radio") ? 80u : 40u);
            props["TagColor"] = item.BindTag ?? "";
            if (type.Contains("Switch")) 
                props["Events"] = new JObject { ["OnStateChanged"] = $"Tags(\"{item.BindTag}\").Write(item.IsAlternateState);" };
            break;

        // --- DATA CONTROLS ---
        case "TrendControl":
        case "AlarmControl":
        case "FunctionTrendControl":
        case "SystemDiagnosisControl":
            props["Left"] = 10 + (dataCtlSlot * 510);
            props["Top"] = 400; // Đặt ở nửa dưới màn hình
            props["Width"] = 500u; props["Height"] = 180u;
            dataCtlSlot++;
            break;

        case "DetailedParameterControl":
            props["Left"] = 10; props["Top"] = 400;
            props["Width"] = 1000u; props["Height"] = 180u;
            break;

        // --- MEDIA & WEB & CONTAINER ---
        case "MediaControl":
        case "WebControl":
        case "ScreenWindow":
            props["Left"] = baseLeft;
            props["Top"] = 380;
            props["Width"] = 240u; props["Height"] = 150u;
            props["Url"] = item.Url ?? "";
            props["ScreenName"] = item.ScreenName ?? "";
            break;

        default:
            return null;
    }

    return new JObject { ["Name"] = itemName, ["Type"] = type, ["Properties"] = props };
}

        /// <summary>
        /// Generates the WinCC ColorScript string for color_on_status behavior.
        /// Keeps JS script generation fully in C# — AI never touches this string.
        /// </summary>
        private static void AddColorScript(JObject props, HmiItemData item)
        {
            if (item.Behaviors.Contains("color_on_status") && !string.IsNullOrEmpty(item.BindTag))
            {
                props["ColorScript"] =
                    $"var status = Tags('{item.BindTag}').Read(); " +
                    $"return status ? HMIRuntime.Math.RGB(0, 255, 0) : HMIRuntime.Math.RGB(255, 0, 0);";
            }
        }

        /// <summary>
        /// Exports HMI tags to CSV — same format as PLC tag CSV so tia tag-hmi can consume it.
        /// </summary>
        private static void ExportHmiTagsCsv(HmiScreenData data)
        {
            try
            {
                // Increment counter for each new CSV file — produces HMI_PLC_Conn_1, HMI_PLC_Conn_2, ...
                _connectionCounter++;
                string connectionName = $"HMI_PLC_Conn_{_connectionCounter}";

                var csv = new StringBuilder();

                // WinCC Unified HMI tag import header — exact column order required by TIA Portal
                csv.AppendLine("Name,Connection,Address,HMI DataType,Acquisition mode,Access Method,Acquisition cycle");

                // Address allocator — starts at MB100 to avoid colliding with existing PLC tags
                int currentByte = 100;
                int currentBit = 0;

                foreach (var tag in data.GlobalTags)
                {
                    string rawType = (tag.Type ?? "BOOL").ToUpper().Trim();
                    string hmiType = ToHmiDataType(rawType);
                    string address = AllocateHmiAddress(rawType, ref currentByte, ref currentBit);

                    // Write-intent tags (marked in comment) use slower T1s cycle, rest use T100ms
                    string cycle = tag.Comment != null && tag.Comment.ToLower().Contains("write")
                        ? "T1s"
                        : "T100ms";

                    csv.AppendLine($"{tag.Name},{connectionName},{address},{hmiType},Cyclic in operation,Absolute access,{cycle}");
                }

                string safeName = string.IsNullOrWhiteSpace(data.ScreenName) ? "AI_Screen" : data.ScreenName;
                string fileName = $"{safeName}_HMI_Tags.csv";
                string fullPath = Path.Combine(OutputPaths.GetGeneratedDir(), fileName);

                // Write with UTF-8 BOM — WinCC Unified CSV import requires it
                File.WriteAllText(fullPath, csv.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

                Console.ForegroundColor = ConsoleColor.Magenta;
                Console.WriteLine($"\n[HMI TAGS] Exported {data.GlobalTags.Count} HMI tags → {fileName}");
                Console.WriteLine($" Connection name: {connectionName}");
                Console.WriteLine($" Run 'tia tag-hmi \"{fullPath}\"' to push tags to TIA Portal.");
                Console.ResetColor();
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"\n[HMI TAGS ERROR]: {ex.Message}");
                Console.ResetColor();
            }
        }

        /// <summary>
        /// Converts PLC data type to WinCC Unified HMI DataType title-casing.
        /// WinCC requires: Bool, Int, UInt, DInt, Real, Word, DWord, Byte — NOT uppercase.
        /// </summary>
        private static string ToHmiDataType(string plcType)
        {
            return plcType switch
            {
                "BOOL" => "Bool",
                "INT" => "Int",
                "UINT" => "UInt",
                "DINT" => "DInt",
                "REAL" => "Real",
                "WORD" => "Word",
                "DWORD" => "DWord",
                "BYTE" => "Byte",
                "SINT" => "SInt",
                _ => "Int"  // Safe fallback
            };
        }

        /// <summary>
        /// Allocates the next available Siemens M-area address for an HMI tag.
        /// Matches the address format in HMITags.csv:
        ///   BOOL        → %M{byte}.{bit}   packed, 8 bits per byte
        ///   INT / WORD  → %MW{byte}        word, 2 bytes, even-aligned
        ///   REAL / DINT → %MD{byte}        dword, 4 bytes, even-aligned
        /// </summary>
        private static string AllocateHmiAddress(string plcType, ref int currentByte, ref int currentBit)
        {
            string address;

            if (plcType == "BOOL")
            {
                address = $"%M{currentByte}.{currentBit}";
                currentBit++;
                if (currentBit > 7) { currentBit = 0; currentByte++; }
            }
            else if (plcType == "REAL" || plcType == "DINT" || plcType == "DWORD" || plcType == "TIME")
            {
                if (currentBit > 0) { currentByte++; currentBit = 0; }   // flush partial bool byte
                if (currentByte % 2 != 0) currentByte++;                  // align to even
                address = $"%MD{currentByte}";
                currentByte += 4;
            }
            else // INT, UINT, WORD, BYTE, SINT, unknown
            {
                if (currentBit > 0) { currentByte++; currentBit = 0; }   // flush partial bool byte
                if (currentByte % 2 != 0) currentByte++;                  // align to even
                address = $"%MW{currentByte}";
                currentByte += 2;
            }

            return address;
        }
    }
}

// ==========================================================================
// CWC SECTION — Models, Normalizer, and Generator
// Handles AI logical JSON → zip package ready to import into TIA Portal
// Manifest format follows real Siemens CWC schema (mver 1.2.0)
// ==========================================================================
namespace TIA_Copilot_CLI
{
    // -----------------------------------------------------------------------
    // DATA MODELS
    // -----------------------------------------------------------------------
    public class CwcParamInfo
    {
        public string Name { get; set; }
        public string Type { get; set; }  // "number" | "boolean" | "string"
    }

    public class CwcPropertyInfo
    {
        public string Name { get; set; }
        public string Type { get; set; }   // "number" | "boolean" | "string"
        public JToken Default { get; set; }   // preserved as JToken to handle any JSON type
        public string Description { get; set; }
    }

    public class CwcEventInfo
    {
        public string Name { get; set; }
        public List<CwcParamInfo> Arguments { get; set; } = new List<CwcParamInfo>();
        public string Description { get; set; }
    }

    public class CwcMethodInfo
    {
        public string Name { get; set; }
        public List<CwcParamInfo> Parameters { get; set; } = new List<CwcParamInfo>();
        public string Description { get; set; }
    }

    public class CwcScreenData
    {
        public string Name { get; set; }
        public string DisplayName { get; set; }
        public string Description { get; set; }
        public List<string> Keywords { get; set; } = new List<string>();
        public List<CwcPropertyInfo> Properties { get; set; } = new List<CwcPropertyInfo>();
        public List<CwcEventInfo> Events { get; set; } = new List<CwcEventInfo>();
        public List<CwcMethodInfo> Methods { get; set; } = new List<CwcMethodInfo>();
        public List<string> ThirdPartyLibs { get; set; } = new List<string>();
        public string HtmlContent { get; set; }
        public string JsContent { get; set; }   // complete code.js including WebCC.start()
        public string CssContent { get; set; }
    }

    // -----------------------------------------------------------------------
    // NORMALIZER: AI logical JSON → CwcScreenData
    // -----------------------------------------------------------------------
    public static class CwcDataNormalizer
    {
        public static CwcScreenData Normalize(JObject root)
        {
            var data = new CwcScreenData();

            var info = root["cwc_info"];
            if (info != null)
            {
                data.Name = info["name"]?.ToString() ?? "AI_Control";
                data.DisplayName = info["displayname"]?.ToString() ?? data.Name;
                data.Description = info["description"]?.ToString() ?? "";

                if (info["keywords"] is JArray kw)
                    foreach (var k in kw) data.Keywords.Add(k.ToString());
            }

            // Properties
            if (root["properties"] is JArray props)
            {
                foreach (JObject p in props)
                {
                    if (!p.ContainsKey("name") || !p.ContainsKey("type")) continue;
                    data.Properties.Add(new CwcPropertyInfo
                    {
                        Name = p["name"]?.ToString(),
                        Type = p["type"]?.ToString() ?? "number",
                        Default = p["default"],   // keep as JToken
                        Description = p["description"]?.ToString() ?? ""
                    });
                }
            }

            // Events (with optional arguments)
            if (root["events"] is JArray events)
            {
                foreach (JObject e in events)
                {
                    if (!e.ContainsKey("name")) continue;
                    var ev = new CwcEventInfo
                    {
                        Name = e["name"].ToString(),
                        Description = e["description"]?.ToString() ?? ""
                    };
                    if (e["arguments"] is JArray args)
                        foreach (JObject a in args)
                            ev.Arguments.Add(new CwcParamInfo
                            {
                                Name = a["name"]?.ToString() ?? "",
                                Type = a["type"]?.ToString() ?? "number"
                            });
                    data.Events.Add(ev);
                }
            }

            // Methods (with optional parameters)
            if (root["methods"] is JArray methods)
            {
                foreach (JObject m in methods)
                {
                    if (!m.ContainsKey("name")) continue;
                    var method = new CwcMethodInfo
                    {
                        Name = m["name"].ToString(),
                        Description = m["description"]?.ToString() ?? ""
                    };
                    if (m["parameters"] is JArray parms)
                        foreach (JObject par in parms)
                            method.Parameters.Add(new CwcParamInfo
                            {
                                Name = par["name"]?.ToString() ?? "",
                                Type = par["type"]?.ToString() ?? "number"
                            });
                    data.Methods.Add(method);
                }
            }

            // Third-party library filenames
            if (root["third_party_libs"] is JArray libs)
                foreach (var lib in libs) data.ThirdPartyLibs.Add(lib.ToString());

            data.HtmlContent = root["html_content"]?.ToString() ?? "";
            data.JsContent = root["js_content"]?.ToString() ?? "";   // complete — no injection needed
            data.CssContent = root["css_content"]?.ToString() ?? "";

            return data;
        }
    }

    // -----------------------------------------------------------------------
    // GENERATOR: CwcScreenData → complete zip package (Siemens mver 1.2.0 format)
    // -----------------------------------------------------------------------
    public static class CwcGenerator
    {
        // Cached path to cwc_assets/ — resolved once via GetAssetsDir()
        private static string _assetsDir = null;

        /// <summary>
        /// Resolves the cwc_assets/ folder using the same walk-up strategy as OutputPaths.
        /// Looks for Translator_CLI/cwc_assets/ first, falls back to BaseDirectory/cwc_assets/.
        /// Prints the resolved path on first call so the user knows exactly where to place files.
        /// </summary>
        private static string GetAssetsDir()
        {
            if (_assetsDir != null) return _assetsDir;

            DirectoryInfo dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (dir != null && !dir.Name.Equals("Translator_CLI", StringComparison.OrdinalIgnoreCase))
                dir = dir.Parent;

            string root = dir != null
                ? dir.FullName
                : AppDomain.CurrentDomain.BaseDirectory;

            _assetsDir = Path.Combine(root, "cwc_assets");

            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"[CWC] Static assets folder: {_assetsDir}");
            Console.ResetColor();

            if (!Directory.Exists(_assetsDir))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[CWC ERROR] cwc_assets/ folder does not exist!");
                Console.WriteLine($"  Create it at: {_assetsDir}");
                Console.WriteLine($"  Required files inside:");
                Console.WriteLine($"    webcc.min.js          (Siemens SWAC engine)");
                Console.WriteLine($"    webcc.d.ts            (TypeScript declarations)");
                Console.WriteLine($"    CWC_manifest_Schema.json (Siemens manifest schema)");
                Console.WriteLine($"    logo.ico              (default icon)");
                Console.WriteLine($"    gauge.min.js, ...     (optional third-party libs)");
                Console.ResetColor();
            }

            return _assetsDir;
        }

        public static void GenerateAndSave(CwcScreenData data)
        {
            try
            {
                // 1. Generate GUID — becomes both the zip filename and the manifest type field
                string guidRaw = Guid.NewGuid().ToString().ToUpper(); // XXXXXXXX-XXXX-XXXX-XXXX-XXXXXXXXXXXX
                string guidFull = $"{{{guidRaw}}}";                    // {XXXXXXXX-...} for zip name
                string safeName = string.IsNullOrWhiteSpace(data.Name) ? "AI_Control" : data.Name;

                // 2. Build manifest.json in real Siemens mver 1.2.0 format
                string manifestJson = BuildManifestJson(data, guidRaw, safeName);

                // 3. Package zip
                string zipFileName = $"{guidFull}.zip";
                string outputDir = OutputPaths.GetGeneratedDir();
                string zipPath = Path.Combine(outputDir, zipFileName);

                using (var zipStream = new FileStream(zipPath, FileMode.Create))
                using (var archive = new System.IO.Compression.ZipArchive(zipStream, System.IO.Compression.ZipArchiveMode.Create))
                {
                    // Root files
                    WriteZipEntry(archive, "manifest.json", manifestJson);
                    CopyStaticAsset(archive, "CWC_manifest_Schema.json", "CWC_manifest_Schema.json");

                    // assets/ — icon
                    CopyStaticAssetWithFallback(archive, new[] { "logo.ico", "logo.png", "icon.png" }, "assets/logo.ico");

                    // control/ — generated files
                    WriteZipEntry(archive, "control/index.html", data.HtmlContent);
                    WriteZipEntry(archive, "control/code.js", data.JsContent);
                    WriteZipEntry(archive, "control/styles.css", data.CssContent);

                    // control/ — static dev helpers
                    CopyStaticAsset(archive, "webcc.d.ts", "control/webcc.d.ts");

                    // control/js/ — always include webcc.min.js
                    CopyStaticAsset(archive, "webcc.min.js", "control/js/webcc.min.js");

                    // control/js/ — third-party libraries AI declared
                    foreach (string lib in data.ThirdPartyLibs)
                        CopyStaticAsset(archive, lib, $"control/js/{lib}");
                }

                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine($"\n[SUCCESS] CWC package exported: {zipFileName}");
                Console.WriteLine($" Control    : {safeName} ({data.DisplayName})");
                Console.WriteLine($" GUID       : {guidFull}");
                Console.WriteLine($" Properties : {data.Properties.Count} | Events: {data.Events.Count} | Methods: {data.Methods.Count}");
                if (data.ThirdPartyLibs.Count > 0)
                    Console.WriteLine($" Libraries  : {string.Join(", ", data.ThirdPartyLibs)}");
                Console.ResetColor();
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine($" Drop into TIA Portal: UserFiles/CustomControls/");
                Console.ResetColor();
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"\n[CWC GENERATOR ERROR]: {ex.Message}");
                Console.ResetColor();
            }
        }

        // -----------------------------------------------------------------------
        // Build manifest.json in real Siemens mver 1.2.0 nested format
        // -----------------------------------------------------------------------
        private static string BuildManifestJson(CwcScreenData data, string guidRaw, string safeName)
        {
            // --- Properties object (keyed by name) ---
            var propsObj = new JObject();
            foreach (var p in data.Properties)
            {
                var propEntry = new JObject();
                propEntry["type"] = p.Type;
                if (p.Default != null)
                    propEntry["default"] = p.Default;
                if (!string.IsNullOrEmpty(p.Description))
                    propEntry["description"] = p.Description;
                propsObj[p.Name] = propEntry;
            }

            // --- Methods object (keyed by name, with parameters) ---
            var methodsObj = new JObject();
            foreach (var m in data.Methods)
            {
                var methodEntry = new JObject();
                if (m.Parameters.Count > 0)
                {
                    var paramsObj = new JObject();
                    foreach (var par in m.Parameters)
                        paramsObj[par.Name] = new JObject { ["type"] = par.Type };
                    methodEntry["parameters"] = paramsObj;
                }
                if (!string.IsNullOrEmpty(m.Description))
                    methodEntry["description"] = m.Description;
                methodsObj[m.Name] = methodEntry;
            }

            // --- Events object (keyed by name, with arguments) ---
            var eventsObj = new JObject();
            foreach (var e in data.Events)
            {
                var eventEntry = new JObject();
                if (e.Arguments.Count > 0)
                {
                    var argsObj = new JObject();
                    foreach (var arg in e.Arguments)
                        argsObj[arg.Name] = new JObject { ["type"] = arg.Type };
                    eventEntry["arguments"] = argsObj;
                }
                if (!string.IsNullOrEmpty(e.Description))
                    eventEntry["description"] = e.Description;
                eventsObj[e.Name] = eventEntry;
            }

            // --- Keywords array ---
            var keywordsArr = new JArray();
            if (data.Keywords.Count > 0)
                foreach (var k in data.Keywords) keywordsArr.Add(k);
            else
                keywordsArr.Add(safeName);

            // --- Assemble the full manifest in Siemens mver 1.2.0 format ---
            var manifest = new JObject
            {
                ["$schema"] = "./CWC_manifest_Schema.json",
                ["mver"] = "1.2.0",
                ["control"] = new JObject
                {
                    ["identity"] = new JObject
                    {
                        ["name"] = safeName,
                        ["version"] = "1.0",
                        ["displayname"] = string.IsNullOrWhiteSpace(data.DisplayName) ? safeName : data.DisplayName,
                        ["icon"] = "./assets/logo.ico",
                        ["type"] = $"guid://{guidRaw}",
                        ["start"] = "./control/index.html"
                    },
                    ["metadata"] = new JObject
                    {
                        ["author"] = "TIA Copilot AI",
                        ["keywords"] = keywordsArr
                    },
                    ["contracts"] = new JObject
                    {
                        ["api"] = new JObject
                        {
                            ["methods"] = methodsObj,
                            ["events"] = eventsObj,
                            ["properties"] = propsObj
                        }
                    }
                }
            };

            return manifest.ToString(Newtonsoft.Json.Formatting.Indented);
        }

        // -----------------------------------------------------------------------
        // Helpers
        // -----------------------------------------------------------------------
        private static void WriteZipEntry(System.IO.Compression.ZipArchive archive, string entryPath, string content)
        {
            var entry = archive.CreateEntry(entryPath, System.IO.Compression.CompressionLevel.Optimal);
            // encoderShouldEmitUTF8Identifier: false → no BOM
            // TIA Portal's JSON parser rejects files that start with the UTF-8 BOM character
            using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            writer.Write(content);
        }

        private static void CopyStaticAsset(System.IO.Compression.ZipArchive archive, string assetFileName, string entryPath)
        {
            string sourcePath = Path.Combine(GetAssetsDir(), assetFileName);
            if (!File.Exists(sourcePath))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[CWC ERROR] Missing static asset: {assetFileName}");
                Console.WriteLine($"  Expected at: {sourcePath}");
                Console.WriteLine($"  Place the file in the cwc_assets/ folder and try again.");
                Console.ResetColor();
                // Throw so the zip generation aborts cleanly rather than producing a broken zip
                throw new FileNotFoundException(
                    $"Required CWC static asset not found: {assetFileName}", sourcePath);
            }

            var entry = archive.CreateEntry(entryPath, System.IO.Compression.CompressionLevel.Optimal);
            using var entryStream = entry.Open();
            using var sourceStream = File.OpenRead(sourcePath);
            sourceStream.CopyTo(entryStream);
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"[CWC] Packed: {assetFileName} → {entryPath}");
            Console.ResetColor();
        }

        /// <summary>
        /// Tries multiple filenames in order (logo.ico → logo.png → icon.png) and uses first found.
        /// Throws if none are found — the icon is mandatory for TIA Portal Toolbox display.
        /// </summary>
        private static void CopyStaticAssetWithFallback(System.IO.Compression.ZipArchive archive,
            string[] candidates, string entryPath)
        {
            foreach (string candidate in candidates)
            {
                string sourcePath = Path.Combine(GetAssetsDir(), candidate);
                if (File.Exists(sourcePath))
                {
                    var entry = archive.CreateEntry(entryPath, System.IO.Compression.CompressionLevel.Optimal);
                    using var entryStream = entry.Open();
                    using var sourceStream = File.OpenRead(sourcePath);
                    sourceStream.CopyTo(entryStream);
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.WriteLine($"[CWC] Packed: {candidate} → {entryPath}");
                    Console.ResetColor();
                    return;
                }
            }

            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[CWC ERROR] No icon file found in cwc_assets/");
            Console.WriteLine($"  Tried: {string.Join(", ", candidates)}");
            Console.WriteLine($"  Place one of these files in: {GetAssetsDir()}");
            Console.ResetColor();
            throw new FileNotFoundException(
                $"No icon found. Tried: {string.Join(", ", candidates)}", GetAssetsDir());
        }
    }
}