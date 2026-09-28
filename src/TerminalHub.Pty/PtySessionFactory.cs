using TerminalHub.Core.Pty;

namespace TerminalHub.Pty;

/// <summary>
/// Picks the real PTY for the current platform: ConPTY on Windows,
/// forkpty on Linux/macOS. Use <see cref="UseMock"/> to force the mock.
/// </summary>
public static class PtySessionFactory
{
    /// <summary>Set by tests or --mock flag to bypass real PTYs.</summary>
    public static bool UseMock { get; set; }

    public static IPtySession Create()
    {
        if (UseMock) return new MockPtySession();
        if (OperatingSystem.IsWindows()) return new ConPtySession();
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS()) return new LinuxPtySession();
        return new MockPtySession();
    }
}
