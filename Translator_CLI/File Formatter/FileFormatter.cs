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