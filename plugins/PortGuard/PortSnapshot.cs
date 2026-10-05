using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace TerminalHub.Official.PortGuard;

/// <summary>One listening TCP socket or wildcard UDP bind from a snapshot.</summary>
public sealed record PortRow(
    string Protocol,
    string LocalAddress,
    int Port,
    string RemoteAddress,
    int Pid,
    string ProcessName,
    bool Udp)
{
    public string StateLabel => Udp ? "UDP 绑定" : "LISTENING";
    public override string ToString()
        => Protocol + "  " + LocalAddress + ":" + Port + "  " + RemoteAddress + "  " + Pid + "  " + ProcessName;
}

public sealed record PortSnapshot(IReadOnlyList<PortRow> Rows, string Source, int ExitCode, string Error);

/// <summary>Pure parsers and decision helpers. No Avalonia, no host, no process launch.</summary>
public static class PortSnapshotParser
{
    public const int SystemPid = 4;

    public static int? PortOf(string localAddress)
    {
        if (string.IsNullOrWhiteSpace(localAddress)) return null;
        var bracket = localAddress.LastIndexOf(']');
        var colon = bracket >= 0 ? localAddress.IndexOf(':', bracket + 1) : localAddress.LastIndexOf(':');
        if (colon < 0 || colon == localAddress.Length - 1) return null;
        return int.TryParse(localAddress[(colon + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out var port)
            && port is >= 0 and <= 65535 ? port : null;
    }

    public static string AddressOf(string localAddress)
    {
        if (string.IsNullOrWhiteSpace(localAddress)) return "";
        var bracket = localAddress.LastIndexOf(']');
        var colon = bracket >= 0 ? localAddress.IndexOf(':', bracket + 1) : localAddress.LastIndexOf(':');
        return colon <= 0 ? localAddress : localAddress[..colon];
    }

    /// <summary>netstat -ano. Columns by whitespace, not header text: protocol, local, remote,
    /// optional state, PID last. Rows whose last column is not an integer (Chinese header, blanks) are skipped.
    /// TCP is kept only when the state text is LISTENING; UDP only when the peer is *:* (ephemeral rows would be killable otherwise).</summary>
    public static IReadOnlyList<PortRow> ParseNetstat(string text)
    {
        var rows = new List<PortRow>();
        foreach (var raw in Lines(text))
        {
            var parts = SplitWs(raw);
            if (parts.Count < 4) continue;
            if (!int.TryParse(parts[^1], NumberStyles.None, CultureInfo.InvariantCulture, out var pid)) continue;
            var protocol = parts[0].ToUpperInvariant();
            var udp = protocol.StartsWith("UDP", StringComparison.Ordinal);
            var tcp = protocol.StartsWith("TCP", StringComparison.Ordinal);
            if (!udp && !tcp) continue;
            if (udp)
            {
                if (parts.Count < 4 || parts[2] != "*:*") continue;
            }
            else
            {
                if (parts.Count < 5 || !parts[3].Equals("LISTENING", StringComparison.OrdinalIgnoreCase)) continue;
            }
            var port = PortOf(parts[1]);
            if (port is not int value) continue;
            rows.Add(new(protocol, AddressOf(parts[1]), value, parts[2], pid, "", udp));
        }
        return rows;
    }

    /// <summary>tasklist /fo csv. Column 1 is the image name, column 2 is the PID.
    /// The header row is skipped because its second column is not an integer (映像名称,PID,…).</summary>
    public static IReadOnlyDictionary<int, string> ParseTasklist(string text)
    {
        var names = new Dictionary<int, string>();
        foreach (var raw in Lines(text))
        {
            var fields = SplitCsv(raw);
            if (fields.Count < 2) continue;
            if (!int.TryParse(fields[1].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var pid)) continue;
            var name = fields[0].Trim();
            if (name.Length == 0) continue;
            names[pid] = name;
        }
        return names;
    }

    public static IReadOnlyList<PortRow> AttachNames(IReadOnlyList<PortRow> rows, IReadOnlyDictionary<int, string> names)
        => rows.Select(row => names.TryGetValue(row.Pid, out var name) && name.Length > 0 ? row with { ProcessName = name } : row).ToArray();

    /// <summary>ss -tulnp and lsof -i -P -n share one rule: TCP only LISTENING,
    /// UDP only wildcard peers (*:*, 0.0.0.0:*, [::]:*).</summary>
    public static IReadOnlyList<PortRow> ParseSs(string text) => ParseUnix(text, ss: true);

    public static IReadOnlyList<PortRow> ParseLsof(string text) => ParseUnix(text, ss: false);

    private static IReadOnlyList<PortRow> ParseUnix(string text, bool ss)
    {
        var rows = new List<PortRow>();
        foreach (var raw in Lines(text))
        {
            var parts = SplitWs(raw);
            if (parts.Count < 2) continue;
            if (ss)
            {
                var protocol = parts[0].ToLowerInvariant();
                var udp = protocol.StartsWith("udp", StringComparison.Ordinal);
                var tcp = protocol.StartsWith("tcp", StringComparison.Ordinal);
                if (!udp && !tcp) continue;
                // State is a token somewhere before the local address, not a fixed column:
                // `ss -o` and similar flags drop the Recv-Q/Send-Q pair, shifting it right.
                if (tcp && !parts.Skip(1).Any(p => p.Equals("LISTEN", StringComparison.OrdinalIgnoreCase)
                    || p.Equals("LISTENING", StringComparison.OrdinalIgnoreCase))) continue;
                var localIndex = IndexOfAddress(parts, 1);
                if (localIndex < 0) continue;
                var remote = localIndex + 1 < parts.Count && LooksLikeAddress(parts[localIndex + 1]) ? parts[localIndex + 1] : "*:*";
                if (udp && !IsWildcardPeer(remote)) continue;
                var port = PortOf(parts[localIndex]);
                if (port is not int value) continue;
                var (pid, name) = ProcessOf(string.Join(' ', parts.Skip(localIndex + 1)));
                rows.Add(new(protocol.ToUpperInvariant(), AddressOf(parts[localIndex]), value, remote, pid, name, udp));
            }
            else
            {
                if (parts[0].Equals("COMMAND", StringComparison.OrdinalIgnoreCase)) continue;
                var protocolIndex = parts.FindIndex(p => p.Equals("TCP", StringComparison.OrdinalIgnoreCase) || p.Equals("UDP", StringComparison.OrdinalIgnoreCase));
                if (protocolIndex < 0 || protocolIndex + 1 >= parts.Count) continue;
                var udp = parts[protocolIndex].Equals("UDP", StringComparison.OrdinalIgnoreCase);
                var nameField = parts[protocolIndex + 1];
                var local = nameField;
                var remote = "";
                var arrow = nameField.IndexOf("->", StringComparison.Ordinal);
                if (arrow >= 0)
                {
                    local = nameField[..arrow];
                    remote = nameField[(arrow + 2)..];
                }
                if (!udp)
                {
                    var state = parts.Count > protocolIndex + 2 ? parts[protocolIndex + 2].Trim('(', ')') : "";
                    if (!state.StartsWith("LISTEN", StringComparison.OrdinalIgnoreCase)) continue;
                }
                else if (arrow >= 0 && !IsWildcardPeer(remote)) continue;
                else if (arrow < 0) remote = "*:*";
                var port = PortOf(local.Contains(':') ? local : nameField);
                if (port is not int value) continue;
                var pid = 0;
                if (parts.Count > 1) int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out pid);
                rows.Add(new(udp ? "UDP" : "TCP", AddressOf(local.Contains(':') ? local : nameField), value,
                    remote.Length == 0 ? "*:*" : remote, pid, parts[0], udp));
            }
        }
        return rows;
    }

    public static bool IsWildcardPeer(string remote)
    {
        var text = remote.Trim();
        if (text is "*:*" or "0.0.0.0:*" or "[::]:*" or "*") return true;
        var port = PortOf(text);
        if (port is null && text.EndsWith(":*", StringComparison.Ordinal)) return AddressOf(text) is "*" or "0.0.0.0" or "[::]" or "::";
        return false;
    }

    public static IReadOnlyList<PortRow> Filter(IReadOnlyList<PortRow> rows, string query)
    {
        var text = (query ?? "").Trim();
        if (text.Length == 0) return rows.ToArray();
        return rows.Where(row =>
            row.Port.ToString(CultureInfo.InvariantCulture).Contains(text, StringComparison.OrdinalIgnoreCase)
            || row.ProcessName.Contains(text, StringComparison.OrdinalIgnoreCase)
            || row.Pid.ToString(CultureInfo.InvariantCulture).Contains(text, StringComparison.OrdinalIgnoreCase)).ToArray();
    }

    public static IReadOnlyList<PortRow> MatchingPort(IReadOnlyList<PortRow> rows, int port)
        => rows.Where(row => row.Port == port).ToArray();

    public static bool TryParsePortQuery(string text, out int port)
    {
        port = 0;
        var value = (text ?? "").Trim();
        return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out port) && port is >= 0 and <= 65535;
    }

    /// <summary>Only an integer PID that appears in the snapshot. Rejects 0, 4, the current process, and anything typed freehand.</summary>
    public static bool CanEndProcess(PortRow? row, IReadOnlyList<PortRow> snapshot, int currentProcessId)
    {
        if (row is null || snapshot is null) return false;
        if (row.Pid is 0 or SystemPid || row.Pid == currentProcessId) return false;
        return snapshot.Any(item => item.Pid == row.Pid);
    }

    public static string ConfirmText(PortRow row, bool windows, bool english = false)
    {
        var port = row.Port.ToString(CultureInfo.InvariantCulture);
        var pid = row.Pid.ToString(CultureInfo.InvariantCulture);
        var name = row.ProcessName.Length == 0 ? (english ? "(unknown)" : "(未知)") : row.ProcessName;
        var tool = windows ? "taskkill /F" : "kill";
        if (english)
            return "End " + row.Protocol + " port " + port + " process " + name + " (PID " + pid + "). "
                + tool + " ends the whole process, not this one port. "
                + "The PID comes from the last snapshot and may have been reused between the snapshot and this click.";
        return "结束 " + row.Protocol + " 端口 " + port + " 的进程 " + name + " (PID " + pid + ")。"
            + tool + " 结束的是整个进程，而不是这一个端口。"
            + "PID 取自上次快照，快照到点击之间可能已被复用。";
    }

    public static (string Shell, string Arguments) DiagnosticSession(PortRow row, bool windows)
    {
        var command = DiagnosticCommand(row, windows);
        return windows
            ? ("cmd.exe", "/d /s /c \"" + command + "\"")
            : ("sh", "-c '" + command.Replace("'", "'\"'\"'", StringComparison.Ordinal) + "'");
    }

    public static string DiagnosticCommand(PortRow row, bool windows)
    {
        var port = row.Port.ToString(CultureInfo.InvariantCulture);
        var pid = row.Pid.ToString(CultureInfo.InvariantCulture);
        var name = row.ProcessName;
        if (windows)
            return "echo Port " + port + " PID " + pid + " " + name + " & netstat -ano | findstr :" + port + " & tasklist /fi \"PID eq " + pid + "\"";
        return "echo Port " + port + " PID " + pid + " " + name + "; ss -tulnp | grep -e :" + port + " -e pid=" + pid + " || lsof -nP -i :" + port;
    }

    public static string ProbeDirectory()
        => Environment.GetFolderPath(Environment.SpecialFolder.System) is { Length: > 0 } system && Directory.Exists(system)
            ? system
            : Environment.CurrentDirectory;

    /// <summary>Windows netstat/tasklist/taskkill speak the OEM code page (936 on this machine).
    /// Unix ss/lsof stay UTF-8. Full-buffer decode so a DBCS pair is not split.</summary>
    public static string DecodeCommandOutput(byte[] bytes, bool windows)
    {
        if (bytes.Length == 0) return "";
        if (!windows || !OperatingSystem.IsWindows()) return Encoding.UTF8.GetString(bytes);
        var codePage = CultureInfo.CurrentCulture.TextInfo.OEMCodePage;
        if (codePage is 65001 or 1200) return Encoding.UTF8.GetString(bytes);
        var count = MultiByteToWideChar((uint)codePage, 0, bytes, bytes.Length, null, 0);
        if (count <= 0) return Encoding.UTF8.GetString(bytes);
        var chars = new char[count];
        var written = MultiByteToWideChar((uint)codePage, 0, bytes, bytes.Length, chars, chars.Length);
        return written > 0 ? new string(chars, 0, written) : Encoding.UTF8.GetString(bytes);
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int MultiByteToWideChar(uint codePage, uint flags, byte[] bytes, int byteCount, char[]? chars, int charCount);

    private static int IndexOfAddress(List<string> parts, int start)
    {
        for (var i = start; i < parts.Count; i++)
            if (LooksLikeAddress(parts[i]) && PortOf(parts[i]) is not null) return i;
        return -1;
    }

    private static bool LooksLikeAddress(string text)
        => text.Contains(':') || text.StartsWith('[') || text is "*" or "*:*";

    private static (int Pid, string Name) ProcessOf(string tail)
    {
        var users = tail.IndexOf("users:(", StringComparison.Ordinal);
        var body = users >= 0 ? tail[users..] : tail;
        var pidMark = body.IndexOf("pid=", StringComparison.Ordinal);
        var pid = 0;
        if (pidMark >= 0)
        {
            var digits = new StringBuilder();
            for (var i = pidMark + 4; i < body.Length && char.IsDigit(body[i]); i++) digits.Append(body[i]);
            int.TryParse(digits.ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out pid);
        }
        var name = "";
        var quote = body.IndexOf('"');
        if (quote >= 0)
        {
            var end = body.IndexOf('"', quote + 1);
            if (end > quote) name = body[(quote + 1)..end];
        }
        return (pid, name);
    }

    private static List<string> Lines(string text)
        => (text ?? "").Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n')
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    private static List<string> SplitWs(string line)
        => line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).ToList();

    private static List<string> SplitCsv(string line)
    {
        var fields = new List<string>();
        var current = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (ch == '"')
            {
                if (quoted && i + 1 < line.Length && line[i + 1] == '"') { current.Append('"'); i++; }
                else quoted = !quoted;
            }
            else if (ch == ',' && !quoted)
            {
                fields.Add(current.ToString());
                current.Clear();
            }
            else current.Append(ch);
        }
        fields.Add(current.ToString());
        return fields;
    }
}
