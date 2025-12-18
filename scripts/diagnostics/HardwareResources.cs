using System;
using Godot;

namespace Diagnostics
{
    public static class HardwareResources
    {

        private const int MinCPUCores = 8;
        private const double MinMemoryMB = 24576;
        private const float MinVRAMMB = 16384;

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
        public static double GetCurrentVRAMMB
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
            var driverInfo = OS.GetVideoAdapterDriverInfo();

            if (driverInfo == null || driverInfo.Length == 0)
            {
                return 0f;
            }

            float vramBytes = driverInfo[0].ToFloat();
            float vramMB = vramBytes / (1024.0f * 1024.0f);
            return vramMB;
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