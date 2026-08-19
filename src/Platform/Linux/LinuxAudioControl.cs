namespace GHelper.Linux.Platform.Linux;

/// <summary>
/// Linux audio control via PipeWire (wpctl).
/// </summary>
public class LinuxAudioControl : IAudioControl
{
    private readonly bool _hasPipewire;

    public LinuxAudioControl()
    {
        _hasPipewire = SysfsHelper.RunCommand("which", "wpctl") != null;
        Helpers.Logger.WriteLine($"Audio system: {(_hasPipewire ? "PipeWire (wpctl)" : "None")}");
    }

    public void ToggleMicMute()
    {
        if (_hasPipewire)
            SysfsHelper.RunCommand("wpctl", "set-mute @DEFAULT_AUDIO_SOURCE@ toggle");
    }

    public bool IsMicMuted()
    {
        if (!_hasPipewire)
            return false;
        var wpOutput = SysfsHelper.RunCommand("wpctl", "get-volume @DEFAULT_AUDIO_SOURCE@");
        return wpOutput?.Contains("[MUTED]") ?? false;
    }

    public void ToggleSpeakerMute()
    {
        if (_hasPipewire)
            SysfsHelper.RunCommand("wpctl", "set-mute @DEFAULT_AUDIO_SINK@ toggle");
    }

    public bool IsSpeakerMuted()
    {
        if (!_hasPipewire)
            return false;
        var wpOutput = SysfsHelper.RunCommand("wpctl", "get-volume @DEFAULT_AUDIO_SINK@");
        return wpOutput?.Contains("[MUTED]") ?? false;
    }
}
