using System;

namespace Diagnostics
{
    public static class HardwareResources
    {

        private const int MinCPUCores = 8;
        private const double MinMemoryMB = 24576;

        public static int GetMinCPUCores
        {
            get { return MinCPUCores; }
        }
        public static double GetMinMemoryMB
        {
            get { return MinMemoryMB; }
        }

        public static string GetHardwareInfo()
        {
            int cpuInfo = GetCPUInfo();
            double memoryInfo = GetMemoryInfo();

            return $"• CPU: {cpuInfo} rdzeni\n• RAM: {memoryInfo / 1024} GB ({memoryInfo} MB)";
        }

        public static bool IfAICapable()
        {
            int cpuCores = GetCPUInfo();
            double totalMemoryMB = GetMemoryInfo();
            return cpuCores >= MinCPUCores && totalMemoryMB >= MinMemoryMB;
        }

        private static int GetCPUInfo()
        {
            int cpuCores = System.Environment.ProcessorCount;
            return cpuCores;
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