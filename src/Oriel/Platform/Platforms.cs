using Oriel.Platform.Linux;
using Oriel.Platform.Windows;

namespace Oriel.Platform;

/// <summary>Picks the backend of the operating system the app runs on.</summary>
internal static class Platforms
{
    public static IPlatform Current()
    {
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763))
            return new WindowsPlatform();
        if (OperatingSystem.IsLinux())
            return new LinuxPlatform();
        return new UnsupportedPlatform();
    }
}

/// <summary>The backend of systems Oriel does not support yet: everything compiles, and running says why it cannot.</summary>
internal sealed class UnsupportedPlatform : IPlatform
{
    public void Run(Action started) =>
        throw new PlatformNotSupportedException("Oriel runs on Windows 10 (1809) and later, and Linux, for now; macOS is planned.");

    public void Post(Action action)
    {
    }

    public void OpenExternally(Uri uri)
    {
    }

    public void Quit()
    {
    }

    public IPlatformWindow OpenWindow(PlatformWindowOptions options, IPlatformWindowEvents events) =>
        throw new PlatformNotSupportedException();
}
