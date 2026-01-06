using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Diagnostics
{
    /// <summary>
    /// Status wykrywania pamięci VRAM na urządzeniu.
    /// </summary>
    public enum VRAMStatus
    {
        /// <summary>Nie wykonano jeszcze detekcji.</summary>
        NotDetected,
        /// <summary>Wykryto konkretną wartość VRAM.</summary>
        Detected,
        /// <summary>GPU korzysta z pamięci współdzielonej.</summary>
        SharedMemory,
        /// <summary>Wystąpił błąd podczas detekcji.</summary>
        Error
    }

    /// <summary>
    /// Narzędzia do odczytu parametrów sprzętowych (CPU, RAM, VRAM) oraz walidacji pod wymagania AI.
    /// </summary>
    /// <remarks>
    /// Statyczna klasa używana w wątku głównym; detekcja VRAM uruchamiana w tle nie jest synchronizowana pod kątem bezpieczeństwa wątków.
    /// </remarks>
    public static class HardwareResources
    {
        // Informacje o VRAM
        /// <summary>
        /// Model przechowujący wynik detekcji VRAM (wartość, status, komunikat).
        /// </summary>
        private class VRAMInfo
        {
            public float valueMB { get; set; }
            public VRAMStatus status { get; set; }
            public string message { get; set; }

            public VRAMInfo()
            {
                valueMB = 0f;
                status = VRAMStatus.NotDetected;
                message = "";
            }
        }

        // Zalecane wymagania sprzętowe
        private const int MinCPUCores = 8;
        private const double MinMemoryMB = 24576;
        private const float MinVRAMMB = 16384;

        // Cache dla VRAM 
        private static VRAMInfo cachedVRAM = null;
        private static bool isVRAMDetectionRunning = false;

        /// <summary>
        /// Minimalna liczba rdzeni CPU rekomendowana do trybu AI.
        /// </summary>
        /// <value>Liczba logicznych rdzeni; stała rekomendacja.</value>
        public static int GetMinCPUCores => MinCPUCores;

        /// <summary>
        /// Minimalna ilość RAM (MB) rekomendowana do trybu AI.
        /// </summary>
        /// <value>Wartość w megabajtach; stała rekomendacja.</value>
        public static double GetMinMemoryMB => MinMemoryMB;

        /// <summary>
        /// Minimalna ilość VRAM (MB) rekomendowana do trybu AI.
        /// </summary>
        /// <value>Wartość w megabajtach; stała rekomendacja.</value>
        public static float GetMinVRAMMB => MinVRAMMB;

        /// <summary>
        /// Zwraca status ostatniej detekcji VRAM (z cache).
        /// </summary>
        /// <value>Aktualny status (NotDetected/Detected/SharedMemory/Error) z pamięci podręcznej.</value>
        public static VRAMStatus VRAMDetectionStatus
        {
            get
            {
                if (cachedVRAM == null)
                    return VRAMStatus.NotDetected;
                return cachedVRAM.status;
            }
        }

        /// <summary>
        /// Uruchamia asynchroniczną detekcję VRAM w tle, jeśli nie jest już uruchomiona.
        /// </summary>
        /// <seealso cref="GetGPUInfo"/>
        public static void StartVRAMDetection()
        {
            if (isVRAMDetectionRunning || cachedVRAM != null)
                return;

            isVRAMDetectionRunning = true;

            // Uruchom zadanie w oddzielnym wątku, asynchroniczne do pobrania VRAM
            Task.Run(() =>
            {
                VRAMInfo vramInfo = GetGPUInfo();
                cachedVRAM = vramInfo;
                isVRAMDetectionRunning = false;
            });
        }

        /// <summary>
        /// Zwraca sformatowane informacje o CPU, RAM i VRAM (z cache VRAM jeśli dostępny).
        /// </summary>
        /// <returns>Opis sprzętu w formie wieloliniowego stringa.</returns>
        /// <seealso cref="GetCPUInfo"/>
        /// <seealso cref="GetMemoryInfo"/>
        public static string GetHardwareInfo()
        {
            int cpuInfo = GetCPUInfo();
            double memoryInfo = GetMemoryInfo();

            if (cachedVRAM != null)
            {
                var vramInfo = cachedVRAM;

                switch (vramInfo.status)
                {
                    case VRAMStatus.Detected:
                        if (vramInfo.valueMB > 0)
                        {
                            return $"• CPU: {cpuInfo} rdzeni\n• RAM: {(memoryInfo / 1024):F2} GB ({memoryInfo:F2} MB)\n• VRAM: {(vramInfo.valueMB / 1024):F2} GB ({vramInfo.valueMB:F2} MB)";
                        }
                        else
                        {
                            return $"• CPU: {cpuInfo} rdzeni\n• RAM: {(memoryInfo / 1024):F2} GB ({memoryInfo:F2} MB)\n• VRAM: Nie wykryto";
                        }
                    case VRAMStatus.SharedMemory:
                        return $"• CPU: {cpuInfo} rdzeni\n• RAM: {(memoryInfo / 1024):F2} GB ({memoryInfo:F2} MB)\n• VRAM: {vramInfo.message}";
                    case VRAMStatus.Error:
                        return $"• CPU: {cpuInfo} rdzeni\n• RAM: {(memoryInfo / 1024):F2} GB ({memoryInfo:F2} MB)\n• VRAM: {vramInfo.message}";
                }
            }

            return $"• CPU: {cpuInfo} rdzeni\n• RAM: {memoryInfo / 1024} GB ({memoryInfo} MB)\n• VRAM: Nieznany status";
        }

        /// <summary>
        /// Sprawdza, czy bieżąca maszyna spełnia minimalne wymagania dla trybu AI (CPU, RAM, VRAM).
        /// </summary>
        /// <returns>True jeśli spełnione są wymagania minimalne.</returns>
        /// <seealso cref="StartVRAMDetection"/>
        /// <seealso cref="GetCPUInfo"/>
        /// <seealso cref="GetMemoryInfo"/>
        public static bool IfAICapable()
        {
            int cpuCores = GetCPUInfo();
            double totalMemoryMB = GetMemoryInfo();

            // Pobierz VRAM z cache lub poczekaj na wykrycie
            float vramMB = 0f;
            if (cachedVRAM != null)
            {
                vramMB = cachedVRAM.valueMB;
            }
            else
            {
                // Uruchom detekcję w tle jeśli jeszcze nie działa
                StartVRAMDetection();

                // Czekaj na zakończenie detekcji
                while (isVRAMDetectionRunning)
                {
                    System.Threading.Thread.Sleep(100);
                }

                // Pobierz wynik detekcji
                if (cachedVRAM != null)
                {
                    vramMB = cachedVRAM.valueMB;
                }
            }

            // Jeśli VRAM jest wystarczający, to pomiń RAM
            if (vramMB >= MinVRAMMB)
            {
                return cpuCores >= MinCPUCores;
            }
            // Jak nie ma wystarczającego VRAM to sprawdź CPU i RAM
            return cpuCores >= MinCPUCores && totalMemoryMB >= MinMemoryMB;
        }

        /// <summary>
        /// Zwraca liczbę logicznych rdzeni procesora.
        /// </summary>
        /// <returns>Liczba rdzeni CPU.</returns>
        private static int GetCPUInfo()
        {
            int cpuCores = Environment.ProcessorCount;
            return cpuCores;
        }

        /// <summary>
        /// Proxy do detekcji VRAM (dla czytelności nazewnictwa GPU).
        /// </summary>
        /// <returns>Struktura VRAMInfo z wynikami detekcji.</returns>
        private static VRAMInfo GetGPUInfo()
        {
            return GetVRAMInfo();
        }

        /// <summary>
        /// Wykrywa ilość VRAM na podstawie platformy (Windows/Linux/macOS) i dostępnych narzędzi CLI.
        /// </summary>
        /// <remarks>Gdy brak sterowników/CLI, zwraca status Error lub SharedMemory.</remarks>
        /// <returns>Struktura VRAMInfo z wartością MB i statusem.</returns>
        private static VRAMInfo GetVRAMInfo()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return GetWindowsVRAM();
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                return GetLinuxVRAM();
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                return GetMacOSVRAM();
            }

            return new VRAMInfo
            {
                valueMB = 0,
                status = VRAMStatus.Error,
                message = "Nieznany system operacyjny"
            };
        }

        /// <summary>
        /// Detekcja VRAM w systemie Windows (nvidia-smi, rocm-smi, xpu-smi, wmic fallback).
        /// </summary>
        /// <returns>Informacje o VRAM lub status błędu/pamięci współdzielonej.</returns>
        private static VRAMInfo GetWindowsVRAM()
        {
            // Metoda 1: NVIDIA-SMI
            string output = RunCommand("nvidia-smi", "--query-gpu=memory.total --format=csv,noheader,nounits");
            if (!string.IsNullOrEmpty(output))
            {
                var match = Regex.Match(output, @"(\d+)");
                if (match.Success && int.TryParse(match.Groups[1].Value, out int vramMB))
                {
                    return new VRAMInfo
                    {
                        valueMB = vramMB,
                        status = VRAMStatus.Detected,
                        message = ""
                    };
                }
            }

            // Metoda 2: AMD
            output = RunCommand("rocm-smi", "--showmeminfo vram");
            if (!string.IsNullOrEmpty(output))
            {
                var amdMatch = Regex.Match(output, @"(\d+)\s*MB");
                if (amdMatch.Success && int.TryParse(amdMatch.Groups[1].Value, out int vramMB))
                {
                    return new VRAMInfo
                    {
                        valueMB = vramMB,
                        status = VRAMStatus.Detected,
                        message = ""
                    };
                }
            }

            // Metoda 3: Intel Arc (xpu-smi lub intel_gpu_top)
            output = RunCommand("xpu-smi", "discovery");
            if (!string.IsNullOrEmpty(output))
            {
                var intelMatch = Regex.Match(output, @"Memory Physical Size:\s*(\d+)\s*MiB", RegexOptions.IgnoreCase);
                if (intelMatch.Success && int.TryParse(intelMatch.Groups[1].Value, out int vramMB))
                {
                    return new VRAMInfo
                    {
                        valueMB = vramMB,
                        status = VRAMStatus.Detected,
                        message = ""
                    };
                }
            }

            // Sprawdzenie zintegrowanej grafiki
            string gpuName = RunCommand("wmic", "path win32_VideoController get Name");
            if (!string.IsNullOrEmpty(gpuName))
            {
                string lowerGpuName = gpuName.ToLower();
                // Intel zintegrowane (UHD, Iris, HD Graphics)
                if (lowerGpuName.Contains("intel") && !lowerGpuName.Contains("arc") && (lowerGpuName.Contains("uhd") || lowerGpuName.Contains("iris") || lowerGpuName.Contains("hd graphics")))
                {
                    return new VRAMInfo
                    {
                        valueMB = 0,
                        status = VRAMStatus.SharedMemory,
                        message = "Pamięć współdzielona"
                    };
                }
                // AMD APU (Radeon Graphics)
                if (lowerGpuName.Contains("amd") && lowerGpuName.Contains("radeon") && lowerGpuName.Contains("graphics"))
                {
                    return new VRAMInfo
                    {
                        valueMB = 0,
                        status = VRAMStatus.SharedMemory,
                        message = "Pamięć współdzielona"
                    };
                }
            }

            return new VRAMInfo
            {
                valueMB = 0,
                status = VRAMStatus.Error,
                message = "Brak danych"
            };
        }

        /// <summary>
        /// Detekcja VRAM w systemach Linux (nvidia-smi, rocm-smi, radeontop, xpu-smi, glxinfo, lspci).
        /// </summary>
        /// <returns>Informacje o VRAM lub status błędu/pamięci współdzielonej.</returns>
        private static VRAMInfo GetLinuxVRAM()
        {
            // NVIDIA
            string nVidiaOutput = RunCommand("nvidia-smi", "--query-gpu=memory.total --format=csv,noheader,nounits");
            if (!string.IsNullOrEmpty(nVidiaOutput))
            {
                var match = Regex.Match(nVidiaOutput, @"(\d+)");
                if (match.Success && int.TryParse(match.Groups[1].Value, out int vramMB))
                {
                    return new VRAMInfo
                    {
                        valueMB = vramMB,
                        status = VRAMStatus.Detected,
                        message = ""
                    };
                }
            }

            // AMD - ROCm
            string amdRocmOutput = RunCommand("rocm-smi", "--showmeminfo vram");
            if (!string.IsNullOrEmpty(amdRocmOutput))
            {
                var match = Regex.Match(amdRocmOutput, @"(\d+)\s*MB");
                if (match.Success && int.TryParse(match.Groups[1].Value, out int vramMB))
                {
                    return new VRAMInfo
                    {
                        valueMB = vramMB,
                        status = VRAMStatus.Detected,
                        message = ""
                    };
                }
            }

            // AMD - radeontop
            string radeontopOutput = RunCommand("sh", "-c \"radeontop -d - -l 1 | grep -i vram\"");
            if (!string.IsNullOrEmpty(radeontopOutput))
            {
                var match = Regex.Match(radeontopOutput, @"vram\s+(\d+)mb", RegexOptions.IgnoreCase);
                if (match.Success && int.TryParse(match.Groups[1].Value, out int vramMB))
                {
                    return new VRAMInfo
                    {
                        valueMB = vramMB,
                        status = VRAMStatus.Detected,
                        message = ""
                    };
                }
            }

            // Intel Arc - xpu-smi
            string intelXpuOutput = RunCommand("xpu-smi", "discovery");
            if (!string.IsNullOrEmpty(intelXpuOutput))
            {
                var match = Regex.Match(intelXpuOutput, @"Memory Physical Size:\s*(\d+)\s*MiB", RegexOptions.IgnoreCase);
                if (match.Success && int.TryParse(match.Groups[1].Value, out int vramMB))
                {
                    return new VRAMInfo
                    {
                        valueMB = vramMB,
                        status = VRAMStatus.Detected,
                        message = ""
                    };
                }
            }

            // Intel Arc - intel_gpu_top (alternatywna metoda)
            string intelGpuTopOutput = RunCommand("sh", "-c \"intel_gpu_top -l | head -20\"");
            if (!string.IsNullOrEmpty(intelGpuTopOutput))
            {
                var match = Regex.Match(intelGpuTopOutput, @"memory:\s*(\d+)\s*MiB", RegexOptions.IgnoreCase);
                if (match.Success && int.TryParse(match.Groups[1].Value, out int vramMB))
                {
                    return new VRAMInfo
                    {
                        valueMB = vramMB,
                        status = VRAMStatus.Detected,
                        message = ""
                    };
                }
            }

            // Sprawdź czy glxinfo jest dostępne
            string whichGlxinfo = RunCommand("sh", "-c \"which glxinfo\"");
            if (!string.IsNullOrEmpty(whichGlxinfo))
            {
                // Próba uniwersalna przez glxinfo
                string glxOutput = RunCommand("sh", "-c \"glxinfo | grep -i 'video memory'\"");
                if (!string.IsNullOrEmpty(glxOutput))
                {
                    var match = Regex.Match(glxOutput, @"(\d+)\s*MB", RegexOptions.IgnoreCase);
                    if (match.Success && int.TryParse(match.Groups[1].Value, out int vramMB))
                    {
                        if (vramMB == 0)
                        {
                            return new VRAMInfo
                            {
                                valueMB = 0,
                                status = VRAMStatus.SharedMemory,
                                message = "Pamięć współdzielona"
                            };
                        }
                        return new VRAMInfo
                        {
                            valueMB = vramMB,
                            status = VRAMStatus.Detected,
                            message = ""
                        };
                    }
                }
            }

            // Sprawdzenie zintegrowanej grafiki
            string lspciOutput = RunCommand("sh", "-c \"lspci | grep -i vga\"");
            if (!string.IsNullOrEmpty(lspciOutput))
            {
                string lowerOutput = lspciOutput.ToLower();
                // Intel zintegrowane
                if (lowerOutput.Contains("intel") && !lowerOutput.Contains("arc") && (lowerOutput.Contains("integrated") || lowerOutput.Contains("uhd") || lowerOutput.Contains("iris") || lowerOutput.Contains("hd graphics")))
                {
                    return new VRAMInfo
                    {
                        valueMB = 0,
                        status = VRAMStatus.SharedMemory,
                        message = "Pamięć współdzielona"
                    };
                }
                // AMD APU
                if (lowerOutput.Contains("amd") && (lowerOutput.Contains("renoir") || lowerOutput.Contains("cezanne") || lowerOutput.Contains("barcelo") || lowerOutput.Contains("rembrandt") || lowerOutput.Contains("picasso") || lowerOutput.Contains("raven")))
                {
                    return new VRAMInfo
                    {
                        valueMB = 0,
                        status = VRAMStatus.SharedMemory,
                        message = "Pamięć współdzielona"
                    };
                }
            }

            return new VRAMInfo
            {
                valueMB = 0,
                status = VRAMStatus.Error,
                message = "Brak danych"
            };
        }

        /// <summary>
        /// Detekcja VRAM w systemie macOS (system_profiler; dla Apple Silicon zwraca SharedMemory).
        /// </summary>
        /// <returns>Informacje o VRAM lub status pamięci współdzielonej.</returns>
        private static VRAMInfo GetMacOSVRAM()
        {
            // macOS System Profiler
            string output = RunCommand("system_profiler", "SPDisplaysDataType");

            if (!string.IsNullOrEmpty(output))
            {
                var match = Regex.Match(output, @"VRAM \(Total\): (\d+)\s*MB|VRAM \(Dynamic, Max\): (\d+)\s*MB", RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    string vramValue = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
                    if (int.TryParse(vramValue, out int vramMB))
                    {
                        return new VRAMInfo
                        {
                            valueMB = vramMB,
                            status = VRAMStatus.Detected,
                            message = ""
                        };
                    }
                }
            }

            // Apple Silicon - domyślnie pamięć współdzielona
            return new VRAMInfo
            {
                valueMB = 0,
                status = VRAMStatus.SharedMemory,
                message = "Pamięć współdzielona"
            };
        }

        /// <summary>
        /// Uruchamia polecenie w tle i zwraca standard output; wymusza lokalizację en_US.
        /// </summary>
        /// <param name="command">Polecenie do wykonania.</param>
        /// <param name="args">Argumenty przekazywane do polecenia.</param>
        /// <returns>Standard output lub pusty string gdy wystąpi błąd/exit code != 0.</returns>
        private static string RunCommand(string command, string args)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = command,
                    Arguments = args,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                // Wymuś język angielski dla wszystkich komunikatów
                psi.EnvironmentVariables["LANG"] = "en_US.UTF-8";
                psi.EnvironmentVariables["LC_ALL"] = "en_US.UTF-8";
                psi.EnvironmentVariables["LANGUAGE"] = "en_US:en";

                using (Process process = Process.Start(psi))
                {
                    string output = process.StandardOutput.ReadToEnd();
                    process.WaitForExit();

                    // Sprawdź exitcode - 0 oznacza sukces
                    if (process.ExitCode != 0)
                    {
                        return string.Empty;
                    }

                    return output;
                }
            }
            catch
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// Zwraca całkowitą dostępną pamięć (RAM) w MB na podstawie GC.
        /// </summary>
        /// <returns>Ilość pamięci w megabajtach.</returns>
        private static double GetMemoryInfo()
        {
            var memoryInfo = GC.GetGCMemoryInfo();
            long totalMemory = memoryInfo.TotalAvailableMemoryBytes;
            double totalMemoryMB = totalMemory / (1024.0 * 1024.0);
            return totalMemoryMB;
        }
    }
}