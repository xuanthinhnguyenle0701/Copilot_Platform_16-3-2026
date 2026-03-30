using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;


namespace TIA_Copilot_CLI
{
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
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string fullPath = Path.Combine(baseDir, fileName);
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
                
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string fullPath = Path.Combine(baseDir, fileName);

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
                data.Width  = screenInfo["width"]?.ToObject<int>()  ?? 1024;
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
                        Name     = item["name"]?.ToString(),
                        Type     = item["type"]?.ToString(),
                        SubType  = item["subtype"]?.ToString(),
                        BindTag  = item["bind_tag"]?.ToString(),
                        Hint     = item["hint"]?.ToString(),
                        Label    = item["label"]?.ToString(),
                        NavigateTo = item["navigate_to"]?.ToString(),
                        Format   = item["format"]?.ToString(),
                        MinValue = item["min_value"]?.ToObject<double?>(),
                        MaxValue = item["max_value"]?.ToObject<double?>(),
                        ClockMode = item["clock_mode"]?.ToString(),
                        Tooltip  = item["tooltip"]?.ToString(),
                        TrendTag = item["trend_tag"]?.ToString(),
                        ShowRuler = item["show_ruler"]?.ToObject<bool>() ?? false,
                        ParameterSetId = item["parameter_set_id"]?.ToObject<int?>(),
                        Url      = item["url"]?.ToString(),
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
                            Tag   = item["keydown_write"]["tag"]?.ToString(),
                            Value = item["keydown_write"]["value"]?.ToObject<int>() ?? 1
                        };

                    if (item["keyup_write"] != null)
                        hmiItem.KeyupWrite = new TagWrite
                        {
                            Tag   = item["keyup_write"]["tag"]?.ToString(),
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
                        Name    = tag["name"]?.ToString()    ?? "",
                        Type    = tag["type"]?.ToString()    ?? "BOOL",
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
        // Increase to push rightward
        private static readonly int SIDEBAR_X       = 30;
        // Increase to push downward for button
        private static readonly int SIDEBAR_Y_START = 180;
        
        private static readonly int SIDEBAR_BTN_W   = 120;
        
        private static readonly int SIDEBAR_BTN_H   = 40;
        private static readonly int SIDEBAR_GAP      = 10;

        private static readonly int PROCESS_X       = 300;
        private static readonly int PROCESS_Y       = 100;

        private static readonly int INDICATOR_X     = 590;
        private static readonly int INDICATOR_Y_START = 160;

        private static readonly int DATACTL_X       = 10;
        private static readonly int DATACTL_Y_START = 10;
        private static readonly int DATACTL_W       = 500;
        private static readonly int DATACTL_H       = 280;
        private static readonly int DATACTL_GAP     = 10;

        public static void GenerateAndSave(HmiScreenData data)
        {
            try
            {
                // Slot counters per zone — increment as objects are placed
                int buttonSlot     = 0;
                int indicatorSlot  = 0;
                int dataCtlSlot    = 0;

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
                    ["ProjectName"]     = "AI_HMI_Project",
                    ["DeviceName"]      = "PC-System_1",
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
                string safeName  = string.IsNullOrWhiteSpace(data.ScreenName) ? "AI_Screen" : data.ScreenName;
                string fileName  = $"{safeName}.json";
                string baseDir   = AppDomain.CurrentDomain.BaseDirectory;
                string fullPath  = Path.Combine(baseDir, fileName);

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

        private static JObject BuildPhysicalItem(
            HmiItemData item,
            ref int buttonSlot,
            ref int indicatorSlot,
            ref int dataCtlSlot)
        {
            var props = new JObject();
            string type = item.Type ?? "";

            switch (type)
            {
                // --- LIBRARY OBJECTS ---
                case "Tank":
                    props["LibraryPath"]       = "IndustryGraphicLibrary/Tanks";
                    props["SubType"]           = "Tank";
                    props["Left"]              = PROCESS_X + 160;
                    props["Top"]               = PROCESS_Y + 35;
                    props["Width"]             = 160;
                    props["Height"]            = 340;
                    props["LevelTag"]          = item.BindTag ?? "";
                    props["DisplayFillLevel"]  = item.Behaviors.Contains("fill_level");
                    if (item.Behaviors.Contains("fill_level"))
                        props["FillLevelColor"] = "255, 161, 0";
                    break;

                case "Valve":
                    props["LibraryPath"] = "IndustryGraphicLibrary/Valves";
                    props["SubType"]     = item.SubType ?? "ControlValve";
                    props["Left"]        = PROCESS_X + 40;
                    props["Top"]         = PROCESS_Y - 25;
                    props["Width"]       = 110;
                    props["Height"]      = 90;
                    props["StatusTag"]   = item.BindTag ?? "";
                    AddColorScript(props, item);
                    break;

                case "Motor":
                    props["LibraryPath"] = "IndustryGraphicLibrary/Motors";
                    props["SubType"]     = item.SubType ?? "Motor2";
                    props["Left"]        = PROCESS_X - 70;
                    props["Top"]         = PROCESS_Y + 335;
                    props["Width"]       = 145;
                    props["Height"]      = 105;
                    props["StatusTag"]   = item.BindTag ?? "";
                    AddColorScript(props, item);
                    break;

                case "Pipe":
                    props["LibraryPath"] = "IndustryGraphicLibrary/Pipes";
                    props["SubType"]     = item.SubType ?? "PipeHorizontal";
                    props["Left"]        = PROCESS_X - 45;
                    props["Top"]         = PROCESS_Y;
                    props["Width"]       = (item.SubType == "PipeVertical") ? 15 : 245;
                    props["Height"]      = (item.SubType == "PipeVertical") ? 315 : 15;
                    props["BasicColor"]  = "238, 238, 238";
                    props["StatusTag"]   = item.BindTag ?? "";
                    break;

                // --- PRIMITIVE SHAPES ---
                case "Rectangle":
                    props["Left"]      = INDICATOR_X;
                    props["Top"]       = INDICATOR_Y_START + (indicatorSlot * 35);
                    props["Width"]     = 25;
                    props["Height"]    = 25;
                    props["StatusTag"] = item.BindTag ?? "";
                    AddColorScript(props, item);
                    indicatorSlot++;
                    break;

                case "Circle":
                    props["CenterX"] = INDICATOR_X + 12;
                    props["CenterY"] = INDICATOR_Y_START + (indicatorSlot * 35) + 12;
                    props["Radius"]  = 12;
                    props["Tag"]     = item.BindTag ?? "";
                    AddColorScript(props, item);
                    indicatorSlot++;
                    break;

                case "CircularArc":
                    props["CenterX"]    = INDICATOR_X + 12;
                    props["CenterY"]    = INDICATOR_Y_START + (indicatorSlot * 35) + 12;
                    props["Radius"]     = 12;
                    props["AngleStart"] = 270;
                    props["AngleRange"] = 90;
                    props["Tag"]        = item.BindTag ?? "";
                    indicatorSlot++;
                    break;

                case "CircleSegment":
                    props["CenterX"]    = INDICATOR_X + 12;
                    props["CenterY"]    = INDICATOR_Y_START + (indicatorSlot * 35) + 12;
                    props["Radius"]     = 12;
                    props["AngleStart"] = 270;
                    props["AngleRange"] = 90;
                    props["Tag"]        = item.BindTag ?? "";
                    indicatorSlot++;
                    break;

                // --- BUTTONS ---
                case "Button":
                    int btnY = SIDEBAR_Y_START + buttonSlot * (SIDEBAR_BTN_H + SIDEBAR_GAP);
                    props["Left"]   = SIDEBAR_X;
                    props["Top"]    = btnY;
                    props["Width"]  = SIDEBAR_BTN_W;
                    props["Height"] = SIDEBAR_BTN_H;
                    props["Text"]   = item.Label ?? item.Name;

                    var scripts = new JObject();
                    if (!string.IsNullOrEmpty(item.NavigateTo))
                    {
                        // Navigation button
                        scripts["KeyUp"] = $"HMIRuntime.UI.SysFct.ChangeScreen('{item.NavigateTo}', null);";
                    }
                    else
                    {
                        // Momentary write button
                        if (item.KeydownWrite != null)
                            scripts["KeyDown"] = $"Tags(\"{item.KeydownWrite.Tag}\").Write({item.KeydownWrite.Value});";
                        if (item.KeyupWrite != null)
                            scripts["KeyUp"] = $"Tags(\"{item.KeyupWrite.Tag}\").Write({item.KeyupWrite.Value});";
                    }
                    props["Scripts"] = scripts;
                    buttonSlot++;
                    break;

                // --- I/O CONTROLS ---
                case "IOField":
                    props["Left"]      = SIDEBAR_X;
                    props["Top"]       = 50;
                    props["Width"]     = 120;
                    props["Height"]    = 40;
                    props["Format"]    = item.Format ?? "{0}";
                    props["StatusTag"] = item.BindTag ?? "";
                    break;

                case "Bar":
                    props["Left"]     = PROCESS_X - 50;
                    props["Top"]      = PROCESS_Y;
                    props["Width"]    = 50;
                    props["Height"]   = 200;
                    props["Tag"]      = item.BindTag ?? "";
                    props["MinValue"] = item.MinValue ?? 0;
                    props["MaxValue"] = item.MaxValue ?? 100;
                    break;

                case "Gauge":
                    props["Left"]     = PROCESS_X + 150;
                    props["Top"]      = PROCESS_Y - 50;
                    props["Width"]    = 150;
                    props["Height"]   = 150;
                    props["Tag"]      = item.BindTag ?? "";
                    props["MinValue"] = item.MinValue ?? 0;
                    props["MaxValue"] = item.MaxValue ?? 100;
                    break;

                case "Clock":
                    props["Left"]      = SIDEBAR_X;
                    props["Top"]       = 20;
                    props["Width"]     = 200;
                    props["Height"]    = 50;
                    props["Format"]    = item.Format ?? "{P, hh:mm:ss}";
                    props["ClockMode"] = item.ClockMode ?? "LocalTime";
                    break;

                case "TouchArea":
                    // Default: overlay over the process center — hint drives final tuning
                    props["Left"]        = PROCESS_X + 160;
                    props["Top"]         = PROCESS_Y + 35;
                    props["Width"]       = 160;
                    props["Height"]      = 340;
                    props["ToolTipText"] = item.Tooltip ?? "";
                    props["Tag"]         = item.BindTag ?? "";
                    break;

                case "CheckBoxGroup":
                    props["Left"]   = SIDEBAR_X;
                    props["Top"]    = SIDEBAR_Y_START + buttonSlot * (SIDEBAR_BTN_H + SIDEBAR_GAP);
                    props["Width"]  = SIDEBAR_BTN_W;
                    props["Height"] = SIDEBAR_BTN_H;
                    props["Text"]   = item.Label ?? item.Name;
                    props["Tag"]    = item.BindTag ?? "";
                    buttonSlot++;
                    break;

                case "RadioButtonGroup":
                    props["Left"]   = SIDEBAR_X;
                    props["Top"]    = SIDEBAR_Y_START + buttonSlot * (SIDEBAR_BTN_H + SIDEBAR_GAP);
                    props["Width"]  = SIDEBAR_BTN_W;
                    props["Height"] = 80;
                    props["Text"]   = item.Label ?? item.Name;
                    props["Tag"]    = item.BindTag ?? "";
                    buttonSlot += 2; // taller slot
                    break;

                // --- DATA CONTROLS ---
                case "TrendControl":
                    props["Left"]      = DATACTL_X + (dataCtlSlot > 0 ? DATACTL_W + DATACTL_GAP : 0);
                    props["Top"]       = DATACTL_Y_START;
                    props["Width"]     = DATACTL_W;
                    props["Height"]    = DATACTL_H;
                    props["TrendName"] = item.TrendTag ?? item.BindTag ?? "";
                    props["ShowRuler"] = item.ShowRuler;
                    dataCtlSlot++;
                    break;

                case "AlarmControl":
                    props["Left"]   = DATACTL_X + (dataCtlSlot > 0 ? DATACTL_W + DATACTL_GAP : 0);
                    props["Top"]    = DATACTL_Y_START;
                    props["Width"]  = DATACTL_W;
                    props["Height"] = DATACTL_H;
                    dataCtlSlot++;
                    break;

                case "FunctionTrendControl":
                    props["Left"]   = DATACTL_X;
                    props["Top"]    = DATACTL_Y_START + DATACTL_H + DATACTL_GAP;
                    props["Width"]  = DATACTL_W - 100;
                    props["Height"] = 250;
                    break;

                case "SystemDiagnosisControl":
                    props["Left"]   = DATACTL_X;
                    props["Top"]    = DATACTL_Y_START;
                    props["Width"]  = DATACTL_W;
                    props["Height"] = DATACTL_H;
                    break;

                case "DetailedParameterControl":
                    props["Left"]           = DATACTL_X + DATACTL_W + DATACTL_GAP;
                    props["Top"]            = DATACTL_Y_START + DATACTL_H + DATACTL_GAP;
                    props["Width"]          = DATACTL_W;
                    props["Height"]         = 250;
                    props["ParameterSetID"] = item.ParameterSetId ?? 1;
                    break;

                // --- MEDIA & WEB ---
                case "MediaControl":
                    props["Left"]   = DATACTL_X;
                    props["Top"]    = DATACTL_Y_START;
                    props["Width"]  = 300;
                    props["Height"] = 200;
                    props["Url"]    = item.Url ?? "";
                    break;

                case "WebControl":
                    props["Left"]   = DATACTL_X + 320;
                    props["Top"]    = DATACTL_Y_START;
                    props["Width"]  = 400;
                    props["Height"] = 300;
                    props["Url"]    = item.Url ?? "";
                    break;

                // --- CONTAINER ---
                case "ScreenWindow":
                    props["Left"]       = DATACTL_X;
                    props["Top"]        = DATACTL_Y_START + DATACTL_H + DATACTL_GAP;
                    props["Width"]      = 300;
                    props["Height"]     = 200;
                    props["ScreenName"] = item.ScreenName ?? "";
                    break;

                default:
                    // [TO BE IMPLEMENTED] — unknown type, skip with warning
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"[HMI WARNING] Unknown item type '{type}' for '{item.Name}' — skipped.");
                    Console.ResetColor();
                    return null;
            }

            return new JObject
            {
                ["Name"]       = item.Name,
                ["Type"]       = type,
                ["Properties"] = props
            };
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
                var csv = new StringBuilder();

                // WinCC Unified HMI tag import header — exact column order required by TIA Portal
                csv.AppendLine("Name,Connection,Address,HMI DataType,Acquisition mode,Access Method,Acquisition cycle");

                // Address allocator — starts at MB100 to avoid colliding with existing PLC tags
                int currentByte = 100;
                int currentBit  = 0;

                foreach (var tag in data.GlobalTags)
                {
                    string rawType = (tag.Type ?? "BOOL").ToUpper().Trim();
                    string hmiType = ToHmiDataType(rawType);
                    string address = AllocateHmiAddress(rawType, ref currentByte, ref currentBit);

                    // Write-intent tags (marked in comment) use slower T1s cycle, rest use T100ms
                    string cycle = tag.Comment != null && tag.Comment.ToLower().Contains("write")
                        ? "T1s"
                        : "T100ms";

                    csv.AppendLine($"{tag.Name},Connection_1,{address},{hmiType},Cyclic in operation,Absolute access,{cycle}");
                }

                string safeName = string.IsNullOrWhiteSpace(data.ScreenName) ? "AI_Screen" : data.ScreenName;
                string fileName = $"{safeName}_HMI_Tags.csv";
                string fullPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, fileName);

                // Write with UTF-8 BOM — WinCC Unified CSV import requires it
                File.WriteAllText(fullPath, csv.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

                Console.ForegroundColor = ConsoleColor.Magenta;
                Console.WriteLine($"\n[HMI TAGS] Exported {data.GlobalTags.Count} HMI tags → {fileName}");
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
                "BOOL"  => "Bool",
                "INT"   => "Int",
                "UINT"  => "UInt",
                "DINT"  => "DInt",
                "REAL"  => "Real",
                "WORD"  => "Word",
                "DWORD" => "DWord",
                "BYTE"  => "Byte",
                "SINT"  => "SInt",
                _       => "Int"  // Safe fallback
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