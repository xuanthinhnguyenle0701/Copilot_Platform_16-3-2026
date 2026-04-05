using System;
using System.IO;

namespace TIA_Copilot_CLI
{
    public static class CwcTiaPipe
    {
        // Subfolder path inside the TIA project folder that WinCC Unified monitors
        private const string CWC_SUBFOLDER = "UserFiles/CustomControls";

        public static void DeployLatestZip(string projectPath)
        {
            // --- Guard: project must be open ---
            if (string.IsNullOrWhiteSpace(projectPath))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("\n[CWC PIPE ERROR] No TIA project is currently open.");
                Console.WriteLine("  Open or create a project first with 'tia open' or 'tia create',");
                Console.WriteLine("  then run 'tia cwc-deploy' again.");
                Console.ResetColor();
                return;
            }

            if (File.Exists(projectPath))
            {
                projectPath = Path.GetDirectoryName(projectPath);
            }

            // --- Find the latest .zip in generated_elements/ ---
            string generatedDir = OutputPaths.GetGeneratedDir();

            var zipFiles = Directory.GetFiles(generatedDir, "*.zip");
            if (zipFiles.Length == 0)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("\n[CWC PIPE ERROR] No generated CWC zip files found.");
                Console.WriteLine($"  Looked in: {generatedDir}");
                Console.WriteLine("  Generate a control first with: chat cwc \"<your request>\"");
                Console.ResetColor();
                return;
            }

            // Pick the most recently created zip
            string latestZip = zipFiles
                .OrderByDescending(f => new FileInfo(f).LastWriteTime)
                .First();

            DeployZip(latestZip, projectPath);
        }

        public static void DeployZip(string zipPath, string projectPath)
        {
            if (string.IsNullOrWhiteSpace(projectPath))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("\n[CWC PIPE ERROR] No TIA project is currently open.");
                Console.WriteLine("  Open or create a project first with 'tia open' or 'tia create'.");
                Console.ResetColor();
                return;
            }

            // If projectPath is a file (like .ap20), get its parent directory
            if (File.Exists(projectPath))
            {
                projectPath = Path.GetDirectoryName(projectPath);
            }

            if (!File.Exists(zipPath))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"\n[CWC PIPE ERROR] Zip file not found: {zipPath}");
                Console.ResetColor();
                return;
            }

            // --- Resolve and create the destination folder ---
            string customControlsDir = Path.Combine(projectPath, CWC_SUBFOLDER.Replace('/', Path.DirectorySeparatorChar));

            try
            {
                if (!Directory.Exists(customControlsDir))
                {
                    Directory.CreateDirectory(customControlsDir);
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.WriteLine($"[CWC PIPE] Created folder: {customControlsDir}");
                    Console.ResetColor();
                }
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"\n[CWC PIPE ERROR] Cannot create CustomControls folder: {ex.Message}");
                Console.WriteLine($"  Target path: {customControlsDir}");
                Console.ResetColor();
                return;
            }

            // --- Copy zip, overwriting if the same GUID was re-generated ---
            string zipFileName = Path.GetFileName(zipPath);
            string destinationPath = Path.Combine(customControlsDir, zipFileName);

            try
            {
                File.Copy(zipPath, destinationPath, overwrite: true);

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"\n[CWC PIPE] Deployed successfully!");
                Console.ResetColor();
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine($"  File     : {zipFileName}");
                Console.WriteLine($"  Copied to: {destinationPath}");
                Console.ResetColor();
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine("  TIA Portal will detect the control automatically.");
                Console.WriteLine("  If the project is open, close and reopen it to refresh the Toolbox.");
                Console.ResetColor();
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"\n[CWC PIPE ERROR] Failed to copy zip: {ex.Message}");
                Console.WriteLine("  If TIA Portal is open, make sure the project is not locked.");
                Console.ResetColor();
            }
        }
    }
}