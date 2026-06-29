using System.Net.Sockets;
using System.Text;

namespace GHelper.Linux.Helpers;

/// <summary>
/// Minimal single-line command channel between ghelper invocations, over a
/// unix socket in XDG_RUNTIME_DIR (user-private, cleaned by logind). Lets a
/// second "ghelper --osk" toggle the on-screen keyboard in the running
/// instance, which is how handheld users bind the keyboard to a hotkey or
/// controller chord. No daemon, no bus: the socket lives and dies with the
/// app process.
/// </summary>
public static class CommandIpc
{
    private static Socket? _server;
    private static string SocketPath =>
        Path.Combine(Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR") ?? "/tmp",
            "ghelper.sock");

    /// <summary>Send one command to a running instance. False when none is
    /// listening (caller then proceeds with a normal startup).</summary>
    public static bool TrySend(string command)
    {
        try
        {
            using var client = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            client.Connect(new UnixDomainSocketEndPoint(SocketPath));
            client.Send(Encoding.UTF8.GetBytes(command + "\n"));
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Start the accept loop. Called once after the single-instance
    /// lock is held, so a leftover socket file is always stale.</summary>
    public static void StartServer(Action<string> onCommand)
    {
        try
        {
            if (File.Exists(SocketPath))
                File.Delete(SocketPath);

            _server = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            _server.Bind(new UnixDomainSocketEndPoint(SocketPath));
            _server.Listen(4);
        }
        catch (Exception ex)
        {
            Logger.WriteLine($"CommandIpc: bind failed: {ex.Message}");
            _server = null;
            return;
        }

        var thread = new Thread(() =>
        {
            var buf = new byte[256];
            while (_server is { } server)
            {
                try
                {
                    using var conn = server.Accept();
                    int n = conn.Receive(buf);
                    if (n <= 0)
                        continue;
                    var command = Encoding.UTF8.GetString(buf, 0, n).Trim();
                    if (command.Length > 0)
                    {
                        Logger.WriteLine($"CommandIpc: received '{command}'");
                        onCommand(command);
                    }
                }
                catch (Exception ex)
                {
                    if (_server == null)
                        return; // Stop() closed the socket
                    Logger.WriteLine($"CommandIpc: accept failed: {ex.Message}");
                    return;
                }
            }
        })
        { IsBackground = true, Name = "ghelper-ipc" };
        thread.Start();
    }

    public static void Stop()
    {
        var server = _server;
        _server = null;
        try
        {
            server?.Close();
            if (File.Exists(SocketPath))
                File.Delete(SocketPath);
        }
        catch { }
    }
}
