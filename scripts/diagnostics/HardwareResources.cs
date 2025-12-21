using System;
using Godot;

namespace Diagnostics
{
    public static class HardwareResources
    {

        private const int MinCPUCores = 8;
        private const double MinMemoryMB = 24576;
        private const float MinVRAMMB = 16384;
        private const string GraphicsCardsFilePath = "res://assets/diagnostics/graphicCards.txt";

        // Cache dla VRAM z bazy danych
        private static float? cachedVRAM = null;
        private static bool isLoadingVRAM = false;

        public static int GetMinCPUCores
        {
            get { return MinCPUCores; }
        }
        public static double GetMinMemoryMB
        {
            get { return MinMemoryMB; }
        }
        public static float GetMinVRAMMB
        {
            get { return MinVRAMMB; }
        }
        public static float GetCurrentVRAMMB
        {
            get { return GetGPUInfo(); }
        }

        public static string GetHardwareInfo()
        {
            int cpuInfo = GetCPUInfo();
            double memoryInfo = GetMemoryInfo();
            float gpuInfo = GetGPUInfo();

            if (gpuInfo != 0f)
            {
                return $"• CPU: {cpuInfo} rdzeni\n• RAM: {memoryInfo / 1024} GB ({memoryInfo} MB)\n• Pamięć VRAM: {gpuInfo / 1024} GB ({gpuInfo} MB)";
            }

            return $"• CPU: {cpuInfo} rdzeni\n• RAM: {memoryInfo / 1024} GB ({memoryInfo} MB)";
        }

        public static bool IfAICapable()
        {
            int cpuCores = GetCPUInfo();
            double totalMemoryMB = GetMemoryInfo();
            float vramMB = GetGPUInfo();

            //Jeśli VRAM jest wystarczający, to pomiń RAM
            if (vramMB >= MinVRAMMB)
            {
                return cpuCores >= MinCPUCores;
            }
            //Jak nie ma informacji o VRAM to sprawdź CPU i RAM
            return cpuCores >= MinCPUCores && totalMemoryMB >= MinMemoryMB;
        }

        private static int GetCPUInfo()
        {
            int cpuCores = System.Environment.ProcessorCount;
            return cpuCores;
        }

        //Do poprawy analizy
        //W razie niewiadomej zwraca 0 (jest pomijane w IfAICapable)
        private static float GetGPUInfo()
        {
            if (cachedVRAM.HasValue)
            {
                return cachedVRAM.Value;
            }

            if (!isLoadingVRAM)
            {
                isLoadingVRAM = true;
                LoadVRAMFromDatabaseAsync();
            }

            return cachedVRAM ?? 0f;
        }

        /// <summary>
        /// Asynchronicznie ładuje VRAM z bazy danych w osobnym wątku
        /// </summary>
        private static async void LoadVRAMFromDatabaseAsync()
        {
            try
            {
                // Wykonaj w osobnym wątku
                float vram = await System.Threading.Tasks.Task.Run(() => GetVRAMFromGraphicsCardDatabase());

                if (vram > 0)
                {
                    cachedVRAM = vram;
                    GD.Print($"📊 VRAM from database cached: {vram} MB");
                }
                else
                {
                    GD.Print("⚠️ VRAM not found in database, using system fallback");
                }
            }
            catch (Exception ex)
            {
                GD.PrintErr($"❌ Error loading VRAM async: {ex.Message}");
            }
            finally
            {
                isLoadingVRAM = false;
            }
        }

        /// <summary>
        /// Wyszukuje VRAM karty graficznej w bazie danych na podstawie nazwy
        /// </summary>
        private static float GetVRAMFromGraphicsCardDatabase()
        {
            try
            {
                // Pobierz nazwę karty graficznej z systemu
                string gpuName = RenderingServer.GetVideoAdapterName();

                if (string.IsNullOrEmpty(gpuName))
                {
                    GD.Print("⚠️ Could not retrieve GPU name from system");
                    return 0f;
                }

                GD.Print($"🔍 Searching for GPU: {gpuName}");

                if (!FileAccess.FileExists(GraphicsCardsFilePath))
                {
                    GD.PrintErr($"❌ Graphics cards database not found: {GraphicsCardsFilePath}");
                    return 0f;
                }

                using var file = FileAccess.Open(GraphicsCardsFilePath, FileAccess.ModeFlags.Read);
                if (file == null)
                {
                    GD.PrintErr("❌ Failed to open graphics cards database");
                    return 0f;
                }

                // Pomiń nagłówek
                file.GetLine();

                // Normalizuj nazwę GPU
                string normalizedGpuName = gpuName.Trim().ToLower();

                while (!file.EofReached())
                {
                    string line = file.GetLine();
                    if (string.IsNullOrWhiteSpace(line))
                        continue;

                    string[] parts = line.Split(',');
                    if (parts.Length < 4)
                        continue;

                    string model = parts[2].Trim();
                    string vramStr = parts[3].Trim();

                    // Sprawdź czy model zawiera się w nazwie GPU z systemu
                    if (normalizedGpuName.Contains(model.ToLower()))
                    {
                        GD.Print($"✅ Found match: {model} -> {vramStr}");
                        return ParseVRAM(vramStr);
                    }
                }

                GD.Print($"⚠️ GPU '{gpuName}' not found in database");
                return 0f;
            }
            catch (Exception ex)
            {
                GD.PrintErr($"❌ Error reading graphics card database: {ex.Message}");
                return 0f;
            }
        }

        /// <summary>
        /// Konwertuje string VRAM na MB
        /// </summary>
        private static float ParseVRAM(string vramStr)
        {
            try
            {
                vramStr = vramStr.Replace("GB", "").Trim();
                if (float.TryParse(vramStr, out float vramGB))
                {
                    return vramGB * 1024f;
                }

                return 0f;
            }
            catch
            {
                return 0f;
            }
        }

        private static double GetMemoryInfo()
        {
            var memoryInfo = GC.GetGCMemoryInfo();
            long totalMemory = memoryInfo.TotalAvailableMemoryBytes;
            double totalMemoryMB = totalMemory / (1024.0 * 1024.0);
            return totalMemoryMB;
        }
    }
}