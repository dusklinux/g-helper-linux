using System.Diagnostics;

namespace GHelper.Linux.Helpers;

/// <summary>
/// Enumerates PipeWire Audio/Source nodes available on the system by
/// shelling out to <c>pw-cli ls Node</c>. Filters out our own helper
/// nodes and monitor sources so the dropdown only shows real microphones.
/// </summary>
public static class AudioSources
{
    public sealed record Source(string NodeName, string Description);

    public static List<Source> Enumerate()
    {
        var list = new List<Source>();
        string output;
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "pw-cli",
                ArgumentList = { "ls", "Node" },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var p = Process.Start(psi);
            if (p == null)
                return list;
            output = p.StandardOutput.ReadToEnd();
            p.WaitForExit(3000);
        }
        catch
        {
            return list;
        }

        // pw-cli emits blocks of properties separated by '\tid N, type ...'
        // headers. Split on those headers and inspect each block.
        var blocks = output.Split(new[] { "\tid " }, StringSplitOptions.None);
        foreach (var b in blocks)
        {
            if (!b.Contains("media.class = \"Audio/Source\""))
                continue;
            var name = Extract(b, "node.name");
            if (name == null)
                continue;
            // Skip our own nodes - the virtual source IS something we
            // create; surfacing it as a selectable capture target would
            // create an infinite loop.
            if (name.StartsWith("ghelper-audio", StringComparison.Ordinal))
                continue;
            // Skip monitor pseudo-sources (.monitor suffix) and effects
            // pre-mixed sources which are not actual mics.
            if (name.EndsWith(".monitor", StringComparison.Ordinal))
                continue;
            var desc = Extract(b, "node.description") ?? name;
            list.Add(new Source(name, desc));
        }
        return list;
    }

    private static string? Extract(string block, string key)
    {
        var marker = $"{key} = \"";
        int i = block.IndexOf(marker, StringComparison.Ordinal);
        if (i < 0)
            return null;
        i += marker.Length;
        int j = block.IndexOf('"', i);
        if (j < 0)
            return null;
        return block.Substring(i, j - i);
    }
}
