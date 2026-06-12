using GHelper.Linux.Helpers;
using GHelper.Linux.Platform;

namespace GHelper.Linux.Fan;

/// <summary>
/// Fan sensor reading, RPM formatting, calibration, and model-specific defaults.
/// All values are in actual RPM. The Linux asus-wmi driver reports actual RPM
/// via fan*_input (the kernel multiplies the EC's RPM/100 value by 100).
/// </summary>
public class FanSensorControl
{
    // All constants in actual RPM
    public const int DefaultFanMin = 1800;
    public const int DefaultFanMax = 5800;
    public const int XgmFanMax = 7200;
    public const int InadequateMax = 10400;
    public const int FanCount = 3; // CPU, GPU, Mid

    private const string KeyFanMax = "fan_max";
    private const string KeyFanRpm = "fan_rpm";

    // Calibration state
    private static int[] _measuredMax = new int[FanCount];
    private static int _sameCount;
    private static System.Timers.Timer? _calibrationTimer;
    private static Action? _onCalibrationComplete;

    // Cached values
    private static int[]? _fanMax;
    private static int[]? _fanMin;

    /// <summary>Whether to display fan speed as RPM instead of percentage.</summary>
    public static bool FanRpm
    {
        get => AppConfig.Is(KeyFanRpm);
        set => AppConfig.Set(KeyFanRpm, value ? 1 : 0);
    }

    /// <summary>Load max fan speeds from config or model defaults. Call once at startup.</summary>
    public static void InitFanMax()
    {
        _fanMax = null;
        _fanMin = null;

        var saved = AppConfig.GetString(KeyFanMax);
        if (!string.IsNullOrEmpty(saved))
        {
            try
            {
                var parts = saved.Split(',');
                if (parts.Length >= FanCount)
                {
                    _fanMax = new int[FanCount];
                    for (int i = 0; i < FanCount; i++)
                        _fanMax[i] = int.Parse(parts[i]);
                    return;
                }
            }
            catch { }
        }

        _fanMax = GetDefaultMax();
    }

    /// <summary>Returns default max fan speeds (actual RPM) for known models.</summary>
    public static int[] GetDefaultMax()
    {
        if (AppConfig.ContainsModel("GA401I"))
            return [7800, 7600, DefaultFanMax];
        if (AppConfig.ContainsModel("GA401"))
            return [7100, 7300, DefaultFanMax];
        if (AppConfig.ContainsModel("GA402"))
            return [5500, 5600, DefaultFanMax];
        if (AppConfig.ContainsModel("G513Q"))
            return [6900, 6900, DefaultFanMax];
        if (AppConfig.ContainsModel("G513R"))
            return [5800, 6000, DefaultFanMax];
        if (AppConfig.ContainsModel("GA503"))
            return [6400, 6400, DefaultFanMax];
        if (AppConfig.ContainsModel("GU603"))
            return [6200, 6400, DefaultFanMax];
        if (AppConfig.ContainsModel("FA507R"))
            return [6300, 5700, DefaultFanMax];
        if (AppConfig.ContainsModel("FA507X"))
            return [6300, 6800, DefaultFanMax];
        if (AppConfig.ContainsModel("FX607J"))
            return [7400, 7200, DefaultFanMax];
        if (AppConfig.ContainsModel("GX650"))
            return [6200, 6200, DefaultFanMax];
        if (AppConfig.ContainsModel("G732"))
            return [6100, 6000, DefaultFanMax];
        if (AppConfig.ContainsModel("G713"))
            return [5600, 6000, DefaultFanMax];
        if (AppConfig.ContainsModel("Z301"))
            return [7200, 6400, DefaultFanMax];
        if (AppConfig.ContainsModel("GV601"))
            return [7800, 5900, 8500];
        if (AppConfig.ContainsModel("GA403"))
            return [6800, 6800, 8000];
        if (AppConfig.ContainsModel("GU605"))
            return [6200, 6200, 9200];
        if (AppConfig.ContainsModel("G614"))
            return [5700, 5100, 4300];
        if (AppConfig.ContainsModel("G634"))
            return [5700, 5100, 4300];
        if (AppConfig.ContainsModel("G814"))
            return [5700, 5100, 4300];
        if (AppConfig.ContainsModel("G834"))
            return [5700, 5100, 4300];
        if (AppConfig.ContainsModel("G614J"))
            return [5700, 5100, 4300];
        if (AppConfig.ContainsModel("G615"))
            return [5700, 5100, 4300];
        if (AppConfig.ContainsModel("G635"))
            return [5700, 5100, 4300];
        if (AppConfig.ContainsModel("G815"))
            return [5700, 5100, 4300];
        if (AppConfig.ContainsModel("G835"))
            return [5700, 5100, 4300];
        if (AppConfig.ContainsModel("HN7306"))
            return [7200, DefaultFanMax, DefaultFanMax];

        return [DefaultFanMax, DefaultFanMax, DefaultFanMax];
    }

    /// <summary>Returns default min fan speeds (actual RPM) for known models.</summary>
    public static int[] GetDefaultMin()
    {
        if (AppConfig.ContainsModel("GA403"))
            return [2200, 2200, DefaultFanMin];
        if (AppConfig.ContainsModel("GU605"))
            return [2200, 2200, DefaultFanMin];
        if (AppConfig.ContainsModel("HN7306"))
            return [2200, DefaultFanMin, DefaultFanMin];

        return [DefaultFanMin, DefaultFanMin, DefaultFanMin];
    }

    /// <summary>Get the max fan speed (actual RPM) for a fan index (0=CPU, 1=GPU, 2=Mid, 3=XGM).</summary>
    public static int GetFanMax(int fan)
    {
        if (fan == 3)
            return XgmFanMax;

        if (_fanMax == null)
            InitFanMax();
        int val = _fanMax![Math.Clamp(fan, 0, FanCount - 1)];

        if (val < DefaultFanMin || val > InadequateMax)
        {
            val = GetDefaultMax()[Math.Clamp(fan, 0, FanCount - 1)];
            _fanMax[fan] = val;
        }

        return val;
    }

    /// <summary>Get the min fan speed (actual RPM) for a fan index.</summary>
    public static int GetFanMin(int fan)
    {
        if (fan == 3)
            return DefaultFanMin;

        if (_fanMin == null)
            _fanMin = GetDefaultMin();
        return _fanMin[Math.Clamp(fan, 0, FanCount - 1)];
    }

    /// <summary>Save a calibrated max fan speed value (actual RPM).</summary>
    public static void SetFanMax(int fan, int value)
    {
        if (_fanMax == null)
            InitFanMax();
        if (fan >= 0 && fan < FanCount)
        {
            _fanMax![fan] = value;
            AppConfig.Set(KeyFanMax, string.Join(",", _fanMax));
        }
    }

    /// <summary>
    /// Format an actual RPM reading as a display string.
    /// Shows RPM directly or as a percentage of the known max.
    /// Auto-updates the stored max if reading exceeds it.
    /// </summary>
    /// <param name="fan">Fan index (0=CPU, 1=GPU, 2=Mid, 3=XGM).</param>
    /// <param name="rpm">Actual fan speed in RPM from GetFanRpm().</param>
    public static string FormatFan(int fan, int rpm)
    {
        if (rpm <= 0)
            return "0";

        int max = GetFanMax(fan);

        // Auto-update max if exceeded
        if (rpm > max && rpm < InadequateMax && fan < FanCount)
        {
            SetFanMax(fan, rpm);
            max = rpm;
        }

        if (FanRpm)
        {
            return $"{rpm}RPM";
        }
        else
        {
            int percent = Math.Clamp((int)Math.Round(100.0 * rpm / max), 0, 100);
            return $"{percent}%";
        }
    }

    /// <summary>Start fan calibration: poll RPM until values stabilize.</summary>
    public static void StartCalibration(IHardwareControl wmi, Action? onComplete = null)
    {
        _onCalibrationComplete = onComplete;
        _sameCount = 0;
        _measuredMax = new int[FanCount];

        _calibrationTimer?.Stop();
        _calibrationTimer = new System.Timers.Timer(1000) { AutoReset = true };
        _calibrationTimer.Elapsed += (_, _) => CalibrationTick(wmi);
        _calibrationTimer.Start();

        Logger.WriteLine("FanSensorControl: calibration started");
    }

    private static void CalibrationTick(IHardwareControl wmi)
    {
        bool changed = false;

        for (int i = 0; i < FanCount; i++)
        {
            int rpm = wmi.GetFanRpm(i);
            if (rpm > _measuredMax[i])
            {
                _measuredMax[i] = rpm;
                changed = true;
            }
        }

        if (changed)
            _sameCount = 0;
        else
            _sameCount++;

        if (_sameCount >= 15)
            FinishCalibration();
    }

    private static void FinishCalibration()
    {
        _calibrationTimer?.Stop();
        _calibrationTimer = null;

        for (int i = 0; i < FanCount; i++)
        {
            int val = _measuredMax[i];
            if (val >= 3000 && val < InadequateMax)
            {
                SetFanMax(i, val);
                Logger.WriteLine($"FanSensorControl: calibrated fan[{i}] max = {val} RPM");
            }
        }

        _onCalibrationComplete?.Invoke();
        _onCalibrationComplete = null;
        Logger.WriteLine("FanSensorControl: calibration finished");
    }
}
