using System.Diagnostics;
using GHelper.Linux.Helpers;

// Tiny end-to-end test of the C# frame parser against the live ghelper-audio
// helper. Spawns the helper, reads frames for ~2 seconds, asserts:
//   - we got at least 40 frames (~20 Hz minimum)
//   - magic + version are correct (would already be caught by AudioFrame parse)
//   - flag bits track an "RNN 0; EQ 1; DLY 1; RVB 1" command sequence
//   - input/output waveforms have finite magnitudes
//
// Run by ../99 wrapper or via `dotnet run` from this dir.

string helperPath = Environment.GetEnvironmentVariable("GHELPER_AUDIO_BIN")
    ?? Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "ghelper-audio");
helperPath = Path.GetFullPath(helperPath);
if (!File.Exists(helperPath))
{
    Console.Error.WriteLine($"helper binary missing: {helperPath}");
    return 2;
}

var psi = new ProcessStartInfo
{
    FileName = helperPath,
    UseShellExecute = false,
    RedirectStandardInput = true,
    RedirectStandardOutput = true,
    RedirectStandardError = true,
    CreateNoWindow = true,
};
using var proc = Process.Start(psi)!;

// Background stderr drain so the helper can't block on a full pipe.
_ = Task.Run(() =>
{
    try
    {
        string? line;
        while ((line = proc.StandardError.ReadLine()) != null)
            Console.Error.WriteLine($"  helper: {line}");
    }
    catch { }
});

// Send a deterministic command sequence after a short warmup so the captured
// frames include both default state and the toggled state.
_ = Task.Run(async () =>
{
    await Task.Delay(800);
    proc.StandardInput.WriteLine("RNN 0");
    proc.StandardInput.WriteLine("EQ 1");
    proc.StandardInput.WriteLine("DLY 1");
    proc.StandardInput.WriteLine("RVB 1");
    proc.StandardInput.Flush();
});

var frame = new AudioFrame();
var reader = new BinaryReader(proc.StandardOutput.BaseStream);

int got = 0;
uint everSeenAllOn = 0;
uint everSeenRnnOff = 0;
float maxIn = 0, maxOut = 0;
var sw = Stopwatch.StartNew();
while (sw.Elapsed < TimeSpan.FromSeconds(2.5) && frame.TryReadFrom(reader))
{
    got++;
    if (frame.Flags == 0b1110) everSeenAllOn = frame.Flags;   // RNN off, EQ+DLY+RVB on
    if (!frame.RnnoiseOn)      everSeenRnnOff = frame.Flags;
    foreach (var v in frame.WaveformIn)  if (Math.Abs(v) > maxIn)  maxIn = Math.Abs(v);
    foreach (var v in frame.WaveformOut) if (Math.Abs(v) > maxOut) maxOut = Math.Abs(v);
}

try
{
    proc.StandardInput.WriteLine("QUIT");
    proc.StandardInput.Flush();
}
catch { }
proc.WaitForExit(1500);
if (!proc.HasExited) proc.Kill();

bool ok = true;
void Check(string name, bool cond)
{
    Console.WriteLine($"  {(cond ? "OK  " : "FAIL")} {name}");
    if (!cond) ok = false;
}

Check($"frames received >= 40 (got {got})", got >= 40);
Check($"max input waveform |x| finite ({maxIn:F3})", float.IsFinite(maxIn));
Check($"max output waveform |x| finite ({maxOut:F3})", float.IsFinite(maxOut));
Check($"observed rnnoise disabled state (flags={everSeenRnnOff:b4})", everSeenRnnOff != 0 || got == 0);
Check($"observed full chain state RNN=0/EQ+DLY+RVB=1 (flags={everSeenAllOn:b4})",
      everSeenAllOn == 0b1110);

Console.WriteLine(ok ? "PASS" : "FAIL");
return ok ? 0 : 1;
