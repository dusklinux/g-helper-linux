namespace GHelper.Linux.Platform.Linux;

public sealed record AttrRange(int Min, int Max, int Step, int Default);

public static class AsusAttributeRange
{
    public static AttrRange? Read(AttrDef attr)
    {
        if (attr == null)
            return null;
        var baseDir = Path.Combine(SysfsHelper.FirmwareAttributes, attr.FwAttrName);
        if (!Directory.Exists(baseDir))
        {
            baseDir = Path.Combine(SysfsHelper.FirmwareAttributes, attr.LegacyName);
            if (!Directory.Exists(baseDir))
                return null;
        }
        int min = ReadInt(Path.Combine(baseDir, "min_value"));
        int max = ReadInt(Path.Combine(baseDir, "max_value"));
        int step = ReadInt(Path.Combine(baseDir, "scalar_increment"), 1);
        int def = ReadInt(Path.Combine(baseDir, "default_value"));
        if (min < 0 && max < 0)
            return null;
        if (step <= 0)
            step = 1;
        return new AttrRange(min, max, step, def);
    }

    private static int ReadInt(string path, int fallback = -1)
    {
        try
        {
            if (!File.Exists(path))
                return fallback;
            var raw = File.ReadAllText(path).Trim();
            return int.TryParse(raw, out int v) ? v : fallback;
        }
        catch { return fallback; }
    }
}
