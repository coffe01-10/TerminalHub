namespace TerminalHub.Core.Sessions;

/// <summary>Session tag pills from the AI-assistant mockup.</summary>
public enum SessionTag
{
    None,
    Dev,
    Test,
    Deploy,
    Codex,
}

public static class SessionTagExtensions
{
    public static string DisplayName(this SessionTag tag) => tag switch
    {
        SessionTag.Dev => "开发环境",
        SessionTag.Test => "测试环境",
        SessionTag.Deploy => "部署控制",
        SessionTag.Codex => "Codex",
        _ => "",
    };

    /// <summary>Dot/pill accent color for the tag.</summary>
    public static string AccentColor(this SessionTag tag) => tag switch
    {
        SessionTag.Dev => "#38BDF8",   // cyan
        SessionTag.Test => "#A78BFA",  // violet
        SessionTag.Deploy => "#F472B6", // pink
        SessionTag.Codex => "#34D399",  // green
        _ => "#64748B",
    };
}
