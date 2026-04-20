using System.Text;
using System.Text.RegularExpressions;
using GHelper.Linux.I18n;

namespace GHelper.Linux.Platform.Linux;

/// <summary>
/// Collects system information from sysfs, procfs, and lspci.
/// All data is readable without root/sudo. Serial numbers and SMART data are excluded.
/// </summary>
public static class SystemInfoCollector
{
    public record InfoEntry(string LabelKey, string Value);
    public record InfoSection(string HeaderKey, List<InfoEntry> Entries);

    private const string DmiPath = "/sys/class/dmi/id";
    private const string CpuSysfsPath = "/sys/devices/system/cpu";
    private const string NetSysfsPath = "/sys/class/net";
    private const string BlockSysfsPath = "/sys/block";
    private const string PowerSupplyPath = "/sys/class/power_supply";
    private const string UsbDevicesPath = "/sys/bus/usb/devices";

    // Cached lspci output (parsed once per collection)
    private static Dictionary<string, LspciDevice>? _lspciCache;

    private record LspciDevice(string Slot, string Class, string Vendor, string Device, string Driver);

    /// <summary>Collect all system information sections.</summary>
    public static List<InfoSection> CollectAll()
    {
        _lspciCache = null; // reset cache per collection
        var sections = new List<InfoSection>();

        sections.Add(CollectSystem());
        sections.Add(CollectCpu());
        sections.Add(CollectMemory());
        sections.Add(CollectGraphics());
        sections.Add(CollectStorage());
        sections.Add(CollectNetwork());
        sections.Add(CollectAudio());
        sections.Add(CollectBattery());
        sections.Add(CollectUsb());

        // Remove sections with no entries
        sections.RemoveAll(s => s.Entries.Count == 0);
        return sections;
    }

    /// <summary>Format sections as plain text for clipboard.</summary>
    public static string ToText(List<InfoSection> sections)
    {
        var sb = new StringBuilder();
        foreach (var section in sections)
        {
            if (sb.Length > 0)
                sb.AppendLine();

            sb.AppendLine($"=== {Labels.Get(section.HeaderKey)} ===");
            int maxLabel = section.Entries.Count > 0
                ? section.Entries.Max(e => Labels.Get(e.LabelKey).Length)
                : 0;

            foreach (var entry in section.Entries)
            {
                string label = Labels.Get(entry.LabelKey);
                sb.AppendLine($"{label.PadRight(maxLabel + 2)}{entry.Value}");
            }
        }
        return sb.ToString().TrimEnd();
    }

    // ---- Section collectors ----

    private static InfoSection CollectSystem()
    {
        var entries = new List<InfoEntry>();

        string product = ReadDmi("product_name");
        if (!string.IsNullOrEmpty(product))
            entries.Add(new("sysinfo_product", product));

        string vendor = ReadDmi("sys_vendor");
        if (!string.IsNullOrEmpty(vendor))
            entries.Add(new("sysinfo_vendor", vendor));

        string board = ReadDmi("board_name");
        if (!string.IsNullOrEmpty(board))
            entries.Add(new("sysinfo_board", board));

        string bios = ReadDmi("bios_version");
        string biosDate = ReadDmi("bios_date");
        if (!string.IsNullOrEmpty(bios))
        {
            if (!string.IsNullOrEmpty(biosDate))
                bios += $" ({biosDate})";
            entries.Add(new("sysinfo_bios", bios));
        }

        string? kernel = ReadFileOneLine("/proc/sys/kernel/osrelease");
        if (!string.IsNullOrEmpty(kernel))
            entries.Add(new("sysinfo_kernel", kernel));

        string os = GetOsName();
        if (!string.IsNullOrEmpty(os))
            entries.Add(new("sysinfo_os", os));

        string desktop = Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP") ?? "";
        if (!string.IsNullOrEmpty(desktop))
            entries.Add(new("sysinfo_desktop", desktop));

        string session = Environment.GetEnvironmentVariable("XDG_SESSION_TYPE") ?? "";
        if (!string.IsNullOrEmpty(session))
            entries.Add(new("sysinfo_session", session));

        return new("sysinfo_system_header", entries);
    }

    private static InfoSection CollectCpu()
    {
        var entries = new List<InfoEntry>();

        // Parse /proc/cpuinfo
        var (modelName, physicalCores, threads, cpuFamily, model, stepping) = ParseProcCpuinfo();

        if (!string.IsNullOrEmpty(modelName))
            entries.Add(new("sysinfo_cpu_model", modelName));

        if (threads > 0)
        {
            string coreInfo = physicalCores > 0 && physicalCores != threads
                ? $"{physicalCores} / {threads}"
                : threads.ToString();
            entries.Add(new("sysinfo_cores_threads", coreInfo));
        }

        // Architecture
        string arch = ReadFileOneLine("/proc/sys/kernel/arch") ?? "";
        if (string.IsNullOrEmpty(arch))
        {
            try
            {
                var uname = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture;
                arch = uname.ToString().ToLowerInvariant();
            }
            catch { }
        }
        if (!string.IsNullOrEmpty(arch))
            entries.Add(new("sysinfo_arch", arch));

        // Frequency range from sysfs
        int minKHz = ReadIntFromFile($"{CpuSysfsPath}/cpu0/cpufreq/cpuinfo_min_freq");
        int maxKHz = ReadIntFromFile($"{CpuSysfsPath}/cpu0/cpufreq/cpuinfo_max_freq");
        if (minKHz > 0 && maxKHz > 0)
        {
            entries.Add(new("sysinfo_frequency", $"{minKHz / 1000} - {maxKHz / 1000} MHz"));
        }
        else if (maxKHz > 0)
        {
            entries.Add(new("sysinfo_frequency", $"{maxKHz / 1000} MHz"));
        }

        // Cache (aggregate per level across all CPUs)
        string cache = ReadCpuCache();
        if (!string.IsNullOrEmpty(cache))
            entries.Add(new("sysinfo_cache", cache));

        // Governor
        string? governor = ReadFileOneLine($"{CpuSysfsPath}/cpu0/cpufreq/scaling_governor");
        if (!string.IsNullOrEmpty(governor))
            entries.Add(new("sysinfo_governor", governor));

        return new("sysinfo_cpu_header", entries);
    }

    private static InfoSection CollectMemory()
    {
        var entries = new List<InfoEntry>();
        var mem = ParseMeminfo();

        if (mem.TotalKB > 0)
            entries.Add(new("sysinfo_total", FormatKB(mem.TotalKB)));
        if (mem.UsedKB > 0)
            entries.Add(new("sysinfo_used", FormatKB(mem.UsedKB)));
        if (mem.AvailableKB > 0)
            entries.Add(new("sysinfo_available", FormatKB(mem.AvailableKB)));
        if (mem.SwapTotalKB > 0)
            entries.Add(new("sysinfo_swap",
                $"{FormatKB(mem.SwapTotalKB)} ({FormatKB(mem.SwapUsedKB)} {Labels.Get("sysinfo_swap_used")})"));

        return new("sysinfo_memory_header", entries);
    }

    private static InfoSection CollectGraphics()
    {
        var entries = new List<InfoEntry>();
        var devices = GetLspciDevices();

        int idx = 1;
        foreach (var dev in devices.Values)
        {
            // Filter for VGA (0300), 3D controller (0302), Display controller (0380)
            if (!dev.Class.Contains("VGA") && !dev.Class.Contains("3D") && !dev.Class.Contains("Display"))
                continue;

            string name = dev.Device;
            if (!string.IsNullOrEmpty(dev.Vendor) && !dev.Device.StartsWith(dev.Vendor, StringComparison.OrdinalIgnoreCase))
                name = $"{dev.Vendor} {dev.Device}";

            string driver = !string.IsNullOrEmpty(dev.Driver) ? dev.Driver : "---";
            string label = $"GPU {idx}";
            entries.Add(new(label, $"{name} ({driver})"));
            idx++;
        }

        // Fallback: enumerate /sys/class/drm/card* if lspci failed
        if (entries.Count == 0)
        {
            entries = CollectGraphicsSysfs();
        }

        return new("sysinfo_graphics_header", entries);
    }

    private static List<InfoEntry> CollectGraphicsSysfs()
    {
        var entries = new List<InfoEntry>();
        try
        {
            var cards = Directory.GetDirectories("/sys/class/drm")
                .Where(d => Regex.IsMatch(Path.GetFileName(d), @"^card\d+$"))
                .OrderBy(d => d);

            int idx = 1;
            foreach (var card in cards)
            {
                string deviceDir = Path.Combine(card, "device");
                if (!Directory.Exists(deviceDir)) continue;

                string vendor = ReadFileOneLine(Path.Combine(deviceDir, "vendor")) ?? "";
                string device = ReadFileOneLine(Path.Combine(deviceDir, "device")) ?? "";

                // Get driver from symlink
                string driver = "";
                string driverLink = Path.Combine(deviceDir, "driver");
                if (Directory.Exists(driverLink))
                {
                    try { driver = Path.GetFileName(Directory.ResolveLinkTarget(driverLink, true)?.FullName ?? ""); }
                    catch { }
                }

                string name = $"{vendor}:{device}";
                if (!string.IsNullOrEmpty(driver))
                    name += $" ({driver})";

                entries.Add(new($"GPU {idx}", name));
                idx++;
            }
        }
        catch { }
        return entries;
    }

    private static InfoSection CollectStorage()
    {
        var entries = new List<InfoEntry>();
        try
        {
            // Read /proc/partitions for drive names and sizes
            string[] lines = File.ReadAllLines("/proc/partitions");
            foreach (string line in lines)
            {
                string trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("major")) continue;

                string[] parts = trimmed.Split(Array.Empty<char>(), StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 4) continue;

                string name = parts[3];
                // Only whole drives: sd[a-z], nvme[0-9]n[0-9], mmcblk[0-9]
                if (!Regex.IsMatch(name, @"^(sd[a-z]+|nvme\d+n\d+|mmcblk\d+)$"))
                    continue;

                if (!long.TryParse(parts[2], out long blocks)) continue;
                double sizeGiB = blocks / (1024.0 * 1024.0); // blocks are 1KB each

                // Model name
                string model = "";
                string deviceModelPath = Path.Combine(BlockSysfsPath, name, "device", "model");
                string deviceNamePath = Path.Combine(BlockSysfsPath, name, "device", "name"); // eMMC
                model = ReadFileOneLine(deviceModelPath) ?? ReadFileOneLine(deviceNamePath) ?? "";

                // Type detection
                string type = DetectDriveType(name);

                string desc = "";
                if (!string.IsNullOrEmpty(model))
                    desc += model.Trim();
                if (sizeGiB > 0)
                {
                    if (desc.Length > 0) desc += ", ";
                    desc += sizeGiB >= 1024 ? $"{sizeGiB / 1024:F1} TiB" : $"{sizeGiB:F1} GiB";
                }
                if (!string.IsNullOrEmpty(type))
                {
                    if (desc.Length > 0) desc += ", ";
                    desc += type;
                }

                entries.Add(new(name, desc));
            }
        }
        catch { }

        return new("sysinfo_storage_header", entries);
    }

    private static InfoSection CollectNetwork()
    {
        var entries = new List<InfoEntry>();
        try
        {
            var interfaces = Directory.GetDirectories(NetSysfsPath)
                .Select(Path.GetFileName)
                .Where(n => n != "lo" && n != null) // skip loopback
                .OrderBy(n => n);

            foreach (string? iface in interfaces)
            {
                if (iface == null) continue;
                string ifDir = Path.Combine(NetSysfsPath, iface);

                // Driver from device/uevent
                string driver = "";
                string ueventPath = Path.Combine(ifDir, "device", "uevent");
                if (File.Exists(ueventPath))
                {
                    try
                    {
                        foreach (string line in File.ReadAllLines(ueventPath))
                        {
                            if (line.StartsWith("DRIVER="))
                            {
                                driver = line["DRIVER=".Length..];
                                break;
                            }
                        }
                    }
                    catch { }
                }

                // State
                string state = ReadFileOneLine(Path.Combine(ifDir, "operstate")) ?? "unknown";

                // Speed (Mbps) - only valid when up
                string speed = "";
                if (state == "up")
                {
                    int speedMbps = ReadIntFromFile(Path.Combine(ifDir, "speed"));
                    if (speedMbps > 0)
                        speed = $"{speedMbps} Mbps";
                }

                // WiFi detection
                bool isWifi = Directory.Exists(Path.Combine(ifDir, "wireless")) ||
                              iface.StartsWith("wl");
                string ifType = isWifi ? "WiFi" : "Ethernet";

                var descParts = new List<string>();
                if (!string.IsNullOrEmpty(driver)) descParts.Add(driver);
                descParts.Add(state);
                if (!string.IsNullOrEmpty(speed)) descParts.Add(speed);
                descParts.Add(ifType);

                entries.Add(new(iface, string.Join(", ", descParts)));
            }
        }
        catch { }

        return new("sysinfo_network_header", entries);
    }

    private static InfoSection CollectAudio()
    {
        var entries = new List<InfoEntry>();

        // Sound cards from /proc/asound/cards
        try
        {
            if (File.Exists("/proc/asound/cards"))
            {
                string[] lines = File.ReadAllLines("/proc/asound/cards");
                foreach (string line in lines)
                {
                    // Format: " 0 [PCH            ]: HDA-Intel - HDA Intel PCH"
                    var match = Regex.Match(line, @"^\s*(\d+)\s+\[.*?\]:\s+(.+)$");
                    if (match.Success)
                    {
                        string cardNum = match.Groups[1].Value;
                        string cardDesc = match.Groups[2].Value.Trim();
                        entries.Add(new($"Card {cardNum}", cardDesc));
                    }
                }
            }
        }
        catch { }

        // Sound server detection (check running processes)
        string server = DetectSoundServer();
        if (!string.IsNullOrEmpty(server))
            entries.Add(new("sysinfo_sound_server", server));

        return new("sysinfo_audio_header", entries);
    }

    private static InfoSection CollectBattery()
    {
        var entries = new List<InfoEntry>();
        try
        {
            if (!Directory.Exists(PowerSupplyPath)) return new("sysinfo_battery_header", entries);

            foreach (string dir in Directory.GetDirectories(PowerSupplyPath))
            {
                string type = ReadFileOneLine(Path.Combine(dir, "type")) ?? "";
                if (type != "Battery") continue;

                string name = Path.GetFileName(dir);

                string status = ReadFileOneLine(Path.Combine(dir, "status")) ?? "---";
                int capacity = ReadIntFromFile(Path.Combine(dir, "capacity"));
                int energyFull = ReadIntFromFile(Path.Combine(dir, "energy_full"));
                int energyDesign = ReadIntFromFile(Path.Combine(dir, "energy_full_design"));
                int powerNow = ReadIntFromFile(Path.Combine(dir, "power_now"));
                int cycles = ReadIntFromFile(Path.Combine(dir, "cycle_count"));

                // Status + capacity
                string statusStr = status;
                if (capacity >= 0)
                    statusStr += $", {capacity}%";
                entries.Add(new("sysinfo_status", statusStr));

                // Health
                if (energyFull > 0 && energyDesign > 0)
                {
                    double health = energyFull * 100.0 / energyDesign;
                    entries.Add(new("sysinfo_health",
                        $"{health:F1}% ({energyFull / 1_000_000.0:F1} / {energyDesign / 1_000_000.0:F1} Wh)"));
                }

                // Power draw
                if (powerNow > 0)
                {
                    entries.Add(new("sysinfo_power", $"{powerNow / 1_000_000.0:F1} W"));
                }

                // Cycle count
                if (cycles >= 0)
                    entries.Add(new("sysinfo_cycles", cycles.ToString()));

                break; // first battery only
            }
        }
        catch { }

        return new("sysinfo_battery_header", entries);
    }

    private static InfoSection CollectUsb()
    {
        var entries = new List<InfoEntry>();
        try
        {
            if (!Directory.Exists(UsbDevicesPath)) return new("sysinfo_usb_header", entries);

            foreach (string dir in Directory.GetDirectories(UsbDevicesPath))
            {
                string dirName = Path.GetFileName(dir);

                // Skip interfaces (contain ':'), root hubs are handled by product check
                if (dirName.Contains(':')) continue;

                // Need a product or manufacturer to be interesting
                string product = ReadFileOneLine(Path.Combine(dir, "product")) ?? "";
                string manufacturer = ReadFileOneLine(Path.Combine(dir, "manufacturer")) ?? "";

                // Skip USB hubs (bDeviceClass 09)
                string devClass = ReadFileOneLine(Path.Combine(dir, "bDeviceClass")) ?? "";
                if (devClass == "09") continue;

                // Skip devices with no useful name
                if (string.IsNullOrEmpty(product) && string.IsNullOrEmpty(manufacturer))
                    continue;

                string idVendor = ReadFileOneLine(Path.Combine(dir, "idVendor")) ?? "";
                string idProduct = ReadFileOneLine(Path.Combine(dir, "idProduct")) ?? "";

                string name = "";
                if (!string.IsNullOrEmpty(manufacturer) && !string.IsNullOrEmpty(product)
                    && !product.StartsWith(manufacturer, StringComparison.OrdinalIgnoreCase))
                    name = $"{manufacturer} {product}";
                else if (!string.IsNullOrEmpty(product))
                    name = product;
                else
                    name = manufacturer;

                string id = (!string.IsNullOrEmpty(idVendor) && !string.IsNullOrEmpty(idProduct))
                    ? $"{idVendor}:{idProduct}"
                    : "";

                string desc = name;
                if (!string.IsNullOrEmpty(id))
                    desc += $" [{id}]";

                entries.Add(new(dirName, desc));
            }

            // Sort by bus-device path
            entries.Sort((a, b) => string.Compare(a.LabelKey, b.LabelKey, StringComparison.Ordinal));
        }
        catch { }

        return new("sysinfo_usb_header", entries);
    }

    // ---- Parsing helpers ----

    private static (string ModelName, int PhysicalCores, int Threads, string Family, string Model, string Stepping)
        ParseProcCpuinfo()
    {
        string modelName = "";
        var coreIds = new HashSet<string>();
        int threads = 0;
        string family = "", model = "", stepping = "";

        try
        {
            if (!File.Exists("/proc/cpuinfo")) return default;

            foreach (string line in File.ReadLines("/proc/cpuinfo"))
            {
                if (line.StartsWith("model name") && string.IsNullOrEmpty(modelName))
                    modelName = ExtractCpuinfoValue(line);
                else if (line.StartsWith("cpu family") && string.IsNullOrEmpty(family))
                    family = ExtractCpuinfoValue(line);
                else if (line.StartsWith("model\t") && string.IsNullOrEmpty(model))
                    model = ExtractCpuinfoValue(line);
                else if (line.StartsWith("stepping") && string.IsNullOrEmpty(stepping))
                    stepping = ExtractCpuinfoValue(line);
                else if (line.StartsWith("processor"))
                    threads++;
                else if (line.StartsWith("core id"))
                    coreIds.Add(ExtractCpuinfoValue(line));
            }
        }
        catch { }

        int physicalCores = coreIds.Count > 0 ? coreIds.Count : threads;
        return (modelName, physicalCores, threads, family, model, stepping);
    }

    private static string ExtractCpuinfoValue(string line)
    {
        int idx = line.IndexOf(':');
        return idx >= 0 ? line[(idx + 1)..].Trim() : "";
    }

    private record MeminfoData(long TotalKB, long UsedKB, long AvailableKB, long SwapTotalKB, long SwapUsedKB);

    private static MeminfoData ParseMeminfo()
    {
        long total = 0, available = 0, free = 0, buffers = 0, cached = 0;
        long swapTotal = 0, swapFree = 0;

        try
        {
            if (!File.Exists("/proc/meminfo")) return new(0, 0, 0, 0, 0);

            foreach (string line in File.ReadLines("/proc/meminfo"))
            {
                if (line.StartsWith("MemTotal:")) total = ParseMeminfoValue(line);
                else if (line.StartsWith("MemAvailable:")) available = ParseMeminfoValue(line);
                else if (line.StartsWith("MemFree:")) free = ParseMeminfoValue(line);
                else if (line.StartsWith("Buffers:")) buffers = ParseMeminfoValue(line);
                else if (line.StartsWith("Cached:")) cached = ParseMeminfoValue(line);
                else if (line.StartsWith("SwapTotal:")) swapTotal = ParseMeminfoValue(line);
                else if (line.StartsWith("SwapFree:")) swapFree = ParseMeminfoValue(line);
            }
        }
        catch { }

        // MemAvailable is preferred (kernel 3.14+), fallback to free+buffers+cached
        if (available == 0 && free > 0)
            available = free + buffers + cached;
        long used = total - available;

        return new(total, used > 0 ? used : 0, available, swapTotal, swapTotal - swapFree);
    }

    private static long ParseMeminfoValue(string line)
    {
        // Format: "MemTotal:       32596716 kB"
        var match = Regex.Match(line, @":\s*(\d+)");
        return match.Success && long.TryParse(match.Groups[1].Value, out long val) ? val : 0;
    }

    private static string ReadCpuCache()
    {
        // Aggregate cache sizes per level, avoiding double-counting shared caches
        var levelSizes = new Dictionary<string, long>(); // "L1d" -> bytes, "L2" -> bytes, etc.
        var seenSharedSets = new Dictionary<string, HashSet<string>>(); // level -> set of shared_cpu_list values

        try
        {
            var cpuDirs = Directory.GetDirectories(CpuSysfsPath)
                .Where(d => Regex.IsMatch(Path.GetFileName(d), @"^cpu\d+$"));

            foreach (string cpuDir in cpuDirs)
            {
                string cachePath = Path.Combine(cpuDir, "cache");
                if (!Directory.Exists(cachePath)) continue;

                foreach (string indexDir in Directory.GetDirectories(cachePath, "index*"))
                {
                    string levelStr = ReadFileOneLine(Path.Combine(indexDir, "level")) ?? "";
                    string typeStr = ReadFileOneLine(Path.Combine(indexDir, "type")) ?? "";
                    string sizeStr = ReadFileOneLine(Path.Combine(indexDir, "size")) ?? "";
                    string sharedCpus = ReadFileOneLine(Path.Combine(indexDir, "shared_cpu_list")) ?? "";

                    if (string.IsNullOrEmpty(levelStr) || string.IsNullOrEmpty(sizeStr)) continue;

                    // Build key: "L1d", "L1i", "L2", "L3"
                    string key = $"L{levelStr}";
                    if (levelStr == "1")
                    {
                        if (typeStr.StartsWith("Data", StringComparison.OrdinalIgnoreCase)) key = "L1d";
                        else if (typeStr.StartsWith("Instruction", StringComparison.OrdinalIgnoreCase)) key = "L1i";
                    }

                    // Skip if we've already counted this shared set
                    if (!seenSharedSets.ContainsKey(key))
                        seenSharedSets[key] = new HashSet<string>();
                    if (!string.IsNullOrEmpty(sharedCpus) && !seenSharedSets[key].Add(sharedCpus))
                        continue; // duplicate shared set

                    long bytes = ParseCacheSize(sizeStr);
                    if (bytes <= 0) continue;

                    levelSizes[key] = levelSizes.GetValueOrDefault(key) + bytes;
                }
            }
        }
        catch { }

        if (levelSizes.Count == 0) return "";

        var parts = new List<string>();
        foreach (string level in new[] { "L1d", "L1i", "L2", "L3" })
        {
            if (levelSizes.TryGetValue(level, out long bytes))
                parts.Add($"{level}: {FormatBytes(bytes)}");
        }

        return string.Join(", ", parts);
    }

    private static long ParseCacheSize(string s)
    {
        // Format: "512K", "32M", "36864K"
        var match = Regex.Match(s, @"(\d+)\s*([KMG])?", RegexOptions.IgnoreCase);
        if (!match.Success || !long.TryParse(match.Groups[1].Value, out long val)) return 0;
        return match.Groups[2].Value.ToUpperInvariant() switch
        {
            "K" => val * 1024,
            "M" => val * 1024 * 1024,
            "G" => val * 1024 * 1024 * 1024,
            _ => val
        };
    }

    private static string DetectDriveType(string name)
    {
        if (name.StartsWith("nvme")) return "NVMe SSD";
        if (name.StartsWith("mmcblk")) return "eMMC";

        // Check rotation for SATA
        int rotation = ReadIntFromFile(Path.Combine(BlockSysfsPath, name, "queue", "rotational"));
        if (rotation == 0) return "SSD";
        if (rotation == 1) return "HDD";

        return "";
    }

    private static string DetectSoundServer()
    {
        // Check /proc for running sound servers
        var servers = new List<string>();
        try
        {
            foreach (string procDir in Directory.GetDirectories("/proc"))
            {
                string dirName = Path.GetFileName(procDir);
                if (!int.TryParse(dirName, out _)) continue;

                string comm = ReadFileOneLine(Path.Combine(procDir, "comm")) ?? "";
                if (comm == "pipewire" && !servers.Contains("PipeWire"))
                    servers.Add("PipeWire");
                else if (comm == "pulseaudio" && !servers.Contains("PulseAudio"))
                    servers.Add("PulseAudio");
                else if ((comm == "jackd" || comm == "jackdbus") && !servers.Contains("JACK"))
                    servers.Add("JACK");
            }
        }
        catch { }

        return servers.Count > 0 ? string.Join(", ", servers) : "";
    }

    private static Dictionary<string, LspciDevice> GetLspciDevices()
    {
        if (_lspciCache != null) return _lspciCache;
        _lspciCache = new Dictionary<string, LspciDevice>();

        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("lspci", "-vmm")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = System.Diagnostics.Process.Start(psi);
            if (proc == null) return _lspciCache;

            string output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(5000);

            // Parse blocks separated by blank lines
            string slot = "", cls = "", vendor = "", device = "", driver = "";
            foreach (string rawLine in output.Split('\n'))
            {
                string line = rawLine.Trim();
                if (string.IsNullOrEmpty(line))
                {
                    if (!string.IsNullOrEmpty(slot))
                        _lspciCache[slot] = new LspciDevice(slot, cls, vendor, device, driver);
                    slot = cls = vendor = device = driver = "";
                    continue;
                }

                int colonIdx = line.IndexOf(':');
                if (colonIdx < 0) continue;
                string key = line[..colonIdx].Trim();
                string val = line[(colonIdx + 1)..].Trim();

                switch (key)
                {
                    case "Slot": slot = val; break;
                    case "Class": cls = val; break;
                    case "Vendor": vendor = val; break;
                    case "Device": device = val; break;
                    case "Driver": driver = val; break;
                }
            }
            // Last block
            if (!string.IsNullOrEmpty(slot))
                _lspciCache[slot] = new LspciDevice(slot, cls, vendor, device, driver);
        }
        catch { }

        return _lspciCache;
    }

    // ---- I/O helpers ----

    private static string ReadDmi(string attr)
    {
        return ReadFileOneLine(Path.Combine(DmiPath, attr)) ?? "";
    }

    private static string? ReadFileOneLine(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            string content = File.ReadAllText(path).Trim();
            return string.IsNullOrEmpty(content) ? null : content;
        }
        catch { return null; }
    }

    private static int ReadIntFromFile(string path)
    {
        string? val = ReadFileOneLine(path);
        return val != null && int.TryParse(val, out int result) ? result : -1;
    }

    private static string GetOsName()
    {
        try
        {
            if (!File.Exists("/etc/os-release")) return "";
            foreach (string line in File.ReadLines("/etc/os-release"))
            {
                if (line.StartsWith("PRETTY_NAME="))
                {
                    string val = line["PRETTY_NAME=".Length..];
                    return val.Trim('"');
                }
            }
        }
        catch { }
        return "";
    }

    private static string FormatKB(long kb)
    {
        if (kb <= 0) return "0";
        double gib = kb / (1024.0 * 1024.0);
        if (gib >= 1.0) return $"{gib:F1} GiB";
        double mib = kb / 1024.0;
        return $"{mib:F0} MiB";
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes >= 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024 * 1024):F0} GiB";
        if (bytes >= 1024L * 1024) return $"{bytes / (1024.0 * 1024):F0} MiB";
        if (bytes >= 1024) return $"{bytes / 1024.0:F0} KiB";
        return $"{bytes} B";
    }
}
