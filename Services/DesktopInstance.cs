using System.IO;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;

namespace XieBox.Services;

// Separate test/data directories can run independently; normal launches reuse the same desktop.
public sealed class DesktopInstance : IDisposable
{
    private readonly Mutex _mutex;
    private readonly EventWaitHandle _activation;
    private RegisteredWaitHandle? _listener;
    public bool IsPrimary { get; }

    public DesktopInstance(string dataDirectory)
    {
        using var currentIdentity = WindowsIdentity.GetCurrent();
        var identity = currentIdentity.User?.Value ?? Environment.UserName;
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity + "|" + Path.GetFullPath(dataDirectory).TrimEnd(Path.DirectorySeparatorChar).ToUpperInvariant())));
        var name = @"Local\XieBox.Desktop." + key;
        _activation = new(false, EventResetMode.AutoReset, name + ".Activate");
        _mutex = new(true, name, out var created);
        IsPrimary = created;
    }

    public void Listen(Action activate)
    {
        if (!IsPrimary || _listener is not null) throw new InvalidOperationException("只有主实例可以监听激活请求。");
        _listener = ThreadPool.RegisterWaitForSingleObject(_activation, (_, _) => activate(), null, Timeout.Infinite, false);
    }

    public void ActivateExisting() => _activation.Set();

    public void Dispose()
    {
        _listener?.Unregister(null);
        _activation.Dispose();
        if (IsPrimary) _mutex.ReleaseMutex();
        _mutex.Dispose();
    }
}
