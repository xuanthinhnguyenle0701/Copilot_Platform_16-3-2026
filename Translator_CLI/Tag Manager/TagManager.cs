using System;
using System.IO;
using System.Text;
using System.Linq;
using ClosedXML.Excel;

namespace TIA_Copilot_CLI
{
    public static class TagManager
    {
        public static string ReadUserTagsCsv(string filePath)
        {
            if (!File.Exists(filePath))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[LỖI] Không tìm thấy file: {filePath}");
                Console.ResetColor();
                return "";
            }

            try
            {
                string[] lines = File.ReadAllLines(filePath);
                if (lines.Length <= 1) return ""; // File trống hoặc chỉ có Header

                // 1. Quét dòng Header để tìm vị trí các cột cần thiết
                string[] headers = lines[0].Split(',');
                int nameIdx = -1, dataTypeIdx = -1, addressIdx = -1;

                for (int i = 0; i < headers.Length; i++)
                {
                    string h = headers[i].Trim().ToLower();
                    if (h == "name") nameIdx = i;
                    else if (h == "data type") dataTypeIdx = i;
                    // BỔ SUNG: Nhận diện cả "logical address" và "address"
                    else if (h == "logical address" || h == "address") addressIdx = i;
                }

                // Nếu thiếu cột Name hoặc Data Type thì báo lỗi ngay
                if (nameIdx == -1 || dataTypeIdx == -1)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("[LỖI] File CSV không đúng chuẩn. Phải có ít nhất cột 'Name' và 'Data Type'.");
                    Console.ResetColor();
                    return "";
                }

                StringBuilder tagListBuilder = new StringBuilder();
                int tagCount = 0;

                // 2. Lặp qua các dòng dữ liệu (bỏ qua dòng 0 - header)
                for (int i = 1; i < lines.Length; i++)
                {
                    if (string.IsNullOrWhiteSpace(lines[i])) continue;

                    string[] columns = lines[i].Split(',');

                    // Bỏ qua nếu dòng bị thiếu cột (rách data)
                    if (columns.Length <= Math.Max(nameIdx, dataTypeIdx)) continue;

                    string tagName = columns[nameIdx].Trim();
                    string dataType = columns[dataTypeIdx].Trim().ToUpper();
                    string address = (addressIdx != -1 && columns.Length > addressIdx) ? columns[addressIdx].Trim() : "";

                    if (string.IsNullOrEmpty(tagName)) continue;

                    // 3. Phân loại Input/Output/Memory dựa trên ký tự đầu của Logical Address
                    string ioType = "";
                    if (address.StartsWith("%I") || address.StartsWith("I")) ioType = " [INPUT]";
                    else if (address.StartsWith("%Q") || address.StartsWith("Q")) ioType = " [OUTPUT]";
                    else if (address.StartsWith("%M") || address.StartsWith("M")) ioType = " [MEMORY]";

                    // 4. Lắp ráp thành đạn: "- TAG_Name (BOOL) [INPUT] at %I0.0"
                    string addressStr = string.IsNullOrEmpty(address) ? "" : $" at {address}";
                    tagListBuilder.AppendLine($"- {tagName} ({dataType}){ioType}{addressStr}");
                    tagCount++;
                }

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"\n[USER TAGS] Đã nạp thành công {tagCount} Tags từ file I/O List (CSV)!");
                Console.ResetColor();

                return tagListBuilder.ToString();
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[LỖI] khi đọc file Tag: {ex.Message}");
                Console.ResetColor();
                return "";
            }
        }

        public static string ReadUserTagsExcel(string filePath)
        {
            if (!File.Exists(filePath))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[LỖI] Không tìm thấy file: {filePath}");
                Console.ResetColor();
                return "";
            }

            try
            {
                StringBuilder tagListBuilder = new StringBuilder();
                int tagCount = 0;

                // Mở file Excel (Chỉ đọc)
                using (var workbook = new XLWorkbook(filePath))
                {
                    // Lấy Sheet đầu tiên
                    var ws = workbook.Worksheet(1);

                    // Lấy dòng Header (giả định là dòng 1)
                    var firstRow = ws.Row(1);

                    int nameCol = -1, dataTypeCol = -1, addressCol = -1;

                    // 1. Quét tìm vị trí các cột
                    // LastCellUsed() giúp không bị quét tràn ra các cột trống
                    for (int i = 1; i <= firstRow.LastCellUsed().Address.ColumnNumber; i++)
                    {
                        string h = firstRow.Cell(i).GetString().Trim().ToLower();
                        if (h == "name") nameCol = i;
                        else if (h == "data type") dataTypeCol = i;
                        // BỔ SUNG: Nhận diện cả "logical address" và "address"
                        else if (h == "logical address" || h == "address") addressCol = i;
                    }

                    if (nameCol == -1 || dataTypeCol == -1)
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine("[LỖI] File Excel không đúng chuẩn. Phải có ít nhất cột 'Name' và 'Data Type' ở dòng đầu tiên.");
                        Console.ResetColor();
                        return "";
                    }

                    // 2. Lặp qua các dòng dữ liệu (Bắt đầu từ dòng 2)
                    var rows = ws.RangeUsed().RowsUsed().Skip(1);

                    foreach (var row in rows)
                    {
                        string tagName = row.Cell(nameCol).GetString().Trim();
                        string dataType = row.Cell(dataTypeCol).GetString().Trim().ToUpper();
                        string address = addressCol != -1 ? row.Cell(addressCol).GetString().Trim() : "";

                        if (string.IsNullOrEmpty(tagName)) continue;

                        // 3. Phân loại Input/Output/Memory
                        string ioType = "";
                        if (address.StartsWith("%I") || address.StartsWith("I")) ioType = " [INPUT]";
                        else if (address.StartsWith("%Q") || address.StartsWith("Q")) ioType = " [OUTPUT]";
                        else if (address.StartsWith("%M") || address.StartsWith("M")) ioType = " [MEMORY]";

                        // 4. Lắp ráp
                        string addressStr = string.IsNullOrEmpty(address) ? "" : $"{address}";
                        tagListBuilder.AppendLine($"- {tagName} ({dataType}){ioType}{addressStr}");
                        tagCount++;
                    }
                }

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"\n[USER TAGS] Đã nạp thành công {tagCount} Tags từ file Excel!");
                Console.ResetColor();

                return tagListBuilder.ToString();
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[LỖI] khi đọc file Excel: {ex.Message}");
                Console.WriteLine("Lưu ý: Hãy chắc chắn file Excel đang không bị mở bởi ứng dụng khác!");
                Console.ResetColor();
                return "";
            }
        }
    }
}