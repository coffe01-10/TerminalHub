using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using TerminalHub.App.Plugins;
using TerminalHub.Core.Pty;
using TerminalHub.Extensibility;
using TerminalHub.Official.PortGuard;
using Xunit;

namespace TerminalHub.Tests;

public class PortGuardPluginTests
{
    private const string Netstat = """
        活动连接

          协议  本地地址          外部地址        状态           PID
          TCP    0.0.0.0:135            0.0.0.0:0              LISTENING       2104
          TCP    10.148.10.110:1024     4.145.79.80:443        ESTABLISHED     7460
          TCP    [::]:53                [::]:0                 LISTENING       7100
          UDP    0.0.0.0:53             *:*                                    7100
          UDP    127.0.0.1:49664        127.0.0.1:49664                        7004
          UDP    [::]:53                *:*                                    7100
        """;

    private const string Tasklist = """
        "映像名称","PID","会话名","会话#","内存使用 "
        "System","4","Services","0","18,560 K"
        "svchost.exe","2104","Services","0","12,000 K"
        "中文进程.exe","7100","Console","1","40,000 K"
        """;

    private const string Ss = """
        Netid State  Recv-Q Send-Q Local Address:Port Peer Address:PortProcess
        udp   UNCONN 0      0      0.0.0.0:53        0.0.0.0:*    users:(("dnsmasq",pid=100,fd=3))
        udp   UNCONN 0      0      127.0.0.1:44000   10.0.0.8:53  users:(("resolver",pid=101,fd=4))
        tcp   LISTEN 0      128    [::]:53           [::]:*       users:(("named",pid=53,fd=5))
        tcp   ESTAB  0      0      10.0.0.8:40000    1.2.3.4:443  users:(("curl",pid=9,fd=6))
        """;

    private const string Lsof = """
        COMMAND   PID USER   FD   TYPE DEVICE SIZE/OFF NODE NAME
        named      53 root   5u  IPv6  1234      0t0  TCP [::]:53 (LISTEN)
        dnsmasq   100 root   3u  IPv4  1235      0t0  UDP 0.0.0.0:53
        curl        9 user   6u  IPv4  1236      0t0  TCP 10.0.0.8:40000->1.2.3.4:443 (ESTABLISHED)
        resolver  101 user   4u  IPv4  1237      0t0  UDP 127.0.0.1:44000->10.0.0.8:53
        """;

    [Fact]
    public void ParseNetstat_ChineseHeader_KeepsListenersAndDropsTheRest()
    {
        var rows = PortSnapshotParser.ParseNetstat(Netstat);
        Assert.Equal(4, rows.Count);
        Assert.Contains(rows, row => row.Protocol == "TCP" && row.Port == 135 && row.Pid == 2104 && row.LocalAddress == "0.0.0.0");
        var v6 = Assert.Single(rows, row => row.Port == 53 && row.Protocol == "TCP");
        Assert.Equal("[::]", v6.LocalAddress);
        Assert.Equal(7100, v6.Pid);
        Assert.DoesNotContain(rows, row => row.RemoteAddress.Contains("4.145.79.80", StringComparison.Ordinal));
        Assert.DoesNotContain(rows, row => row.Pid == 7004);
        Assert.Equal(2, rows.Count(row => row.Udp && row.RemoteAddress == "*:*"));
    }

    [Fact]
    public void ParseTasklist_ChineseHeader_MapsImageNameByPid()
    {
        var names = PortSnapshotParser.ParseTasklist(Tasklist);
        Assert.Equal("System", names[4]);
        Assert.Equal("中文进程.exe", names[7100]);
        Assert.DoesNotContain(names.Values, name => name == "映像名称");
        var attached = PortSnapshotParser.AttachNames(PortSnapshotParser.ParseNetstat(Netstat), names);
        Assert.Equal("中文进程.exe", Assert.Single(attached, row => row.Port == 53 && row.Protocol == "TCP").ProcessName);
    }

    [Fact]
    public void PortOf_BracketedIpv6_TakesThePortAfterTheBracket()
    {
        Assert.Equal(53, PortSnapshotParser.PortOf("[::]:53"));
        Assert.Equal("[::]", PortSnapshotParser.AddressOf("[::]:53"));
        Assert.Equal(46023, PortSnapshotParser.PortOf("[fd7a:115c:a1e0::f83b:b35c]:46023"));
    }

    [Fact]
    public void ParseSsAndLsof_KeepListeningTcpAndWildcardUdpOnly()
    {
        var ss = PortSnapshotParser.ParseSs(Ss);
        Assert.Equal(2, ss.Count);
        Assert.Contains(ss, row => row.Udp && row.Port == 53 && row.Pid == 100 && row.ProcessName == "dnsmasq");
        Assert.Contains(ss, row => !row.Udp && row.Port == 53 && row.Pid == 53 && row.ProcessName == "named");
        Assert.DoesNotContain(ss, row => row.Pid == 9 || row.Pid == 101);

        var lsof = PortSnapshotParser.ParseLsof(Lsof);
        Assert.Equal(2, lsof.Count);
        Assert.Contains(lsof, row => !row.Udp && row.Port == 53 && row.Pid == 53 && row.ProcessName == "named");
        Assert.Contains(lsof, row => row.Udp && row.Port == 53 && row.Pid == 100);
        Assert.DoesNotContain(lsof, row => row.Pid == 9 || row.Pid == 101);
    }

    private const string SsWithoutQueueColumns = """
        Netid State Local Address:Port Peer Address:PortProcess
        tcp   LISTEN 0.0.0.0:8080      0.0.0.0:*    users:(("web",pid=200,fd=7))
        tcp   ESTAB  10.0.0.8:50000    1.2.3.4:443  users:(("curl",pid=9,fd=8))
        udp   UNCONN 0.0.0.0:53        0.0.0.0:*    users:(("dns",pid=100,fd=9))
        """;

    [Fact]
    public void ParseSs_WithoutQueueColumns_StillFindsListenState()
    {
        // ss -o drops Recv-Q/Send-Q; State must be found as a token, not by column index.
        var rows = PortSnapshotParser.ParseSs(SsWithoutQueueColumns);
        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, row => !row.Udp && row.Port == 8080 && row.Pid == 200 && row.ProcessName == "web");
        Assert.Contains(rows, row => row.Udp && row.Port == 53 && row.Pid == 100);
        Assert.DoesNotContain(rows, row => row.Pid == 9);
    }

    [Fact]
    public void ConfirmEndWindow_TitleBarClose_CountsAsCancel()
    {
        // ConfirmEndWindow is internal; find it by name and require an OnClosing override so a
        // title-bar X lands as false instead of bypassing the caller's `!= true` check.
        var type = typeof(PortGuardPlugin).Assembly.GetType("TerminalHub.Official.PortGuard.ConfirmEndWindow");
        Assert.NotNull(type);
        var method = type!.GetMethod("OnClosing", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        Assert.True(method!.IsFamily || method.IsFamilyOrAssembly, "OnClosing must be overridden to force a cancel result");
        Assert.Equal(type, method.DeclaringType); // overridden here, not inherited from Window
    }

    [Fact]
    public void Filter_MatchesPortProcessOrPid_IgnoringCase()
    {
        var rows = PortSnapshotParser.AttachNames(PortSnapshotParser.ParseNetstat(Netstat), PortSnapshotParser.ParseTasklist(Tasklist));
        Assert.Equal(rows.Count, PortSnapshotParser.Filter(rows, "  ").Count);
        Assert.Contains(PortSnapshotParser.Filter(rows, "中文"), row => row.Pid == 7100);
        Assert.Single(PortSnapshotParser.Filter(rows, "2104"));
        Assert.Equal(3, PortSnapshotParser.Filter(rows, "53").Count);
        Assert.Empty(PortSnapshotParser.Filter(rows, "does-not-exist"));
    }

    [Fact]
    public void CanEndProcess_RejectsZeroSystemAndCurrent_AndFreeText()
    {
        var rows = new List<PortRow>
        {
            new("TCP", "0.0.0.0", 135, "0.0.0.0:0", 0, "idle", false),
            new("TCP", "0.0.0.0", 445, "0.0.0.0:0", 4, "System", false),
            new("TCP", "127.0.0.1", 9, "0.0.0.0:0", Environment.ProcessId, "tests", false),
            new("TCP", "127.0.0.1", 8080, "0.0.0.0:0", 4242, "dev.exe", false)
        };
        Assert.False(PortSnapshotParser.CanEndProcess(rows[0], rows, Environment.ProcessId));
        Assert.False(PortSnapshotParser.CanEndProcess(rows[1], rows, Environment.ProcessId));
        Assert.False(PortSnapshotParser.CanEndProcess(rows[2], rows, Environment.ProcessId));
        Assert.True(PortSnapshotParser.CanEndProcess(rows[3], rows, Environment.ProcessId));
        var typed = new PortRow("TCP", "127.0.0.1", 1, "*:*", 99999, "typed", false);
        Assert.False(PortSnapshotParser.CanEndProcess(typed, rows, Environment.ProcessId));
        Assert.False(PortSnapshotParser.CanEndProcess(null, rows, Environment.ProcessId));
    }

    [Fact]
    public void ConfirmText_NamesProtocolPortProcessAndWarnsAboutReuse()
    {
        var row = new PortRow("UDP", "[::]", 53, "*:*", 7100, "中文进程.exe", true);
        var zh = PortSnapshotParser.ConfirmText(row, windows: true);
        Assert.Contains("UDP", zh, StringComparison.Ordinal);
        Assert.Contains("53", zh, StringComparison.Ordinal);
        Assert.Contains("中文进程.exe", zh, StringComparison.Ordinal);
        Assert.Contains("7100", zh, StringComparison.Ordinal);
        Assert.Contains("taskkill /F", zh, StringComparison.Ordinal);
        Assert.Contains("整个进程", zh, StringComparison.Ordinal);
        Assert.Contains("已被复用", zh, StringComparison.Ordinal);
        var en = PortSnapshotParser.ConfirmText(row, windows: false, english: true);
        Assert.Contains("kill", en, StringComparison.Ordinal);
        Assert.Contains("whole process", en, StringComparison.Ordinal);
        Assert.Contains("reused", en, StringComparison.Ordinal);
    }

    [Fact]
    public void DiagnosticSession_UsesCmdOnWindowsAndShElsewhere()
    {
        var row = new PortRow("TCP", "127.0.0.1", 8080, "0.0.0.0:0", 4242, "dev.exe", false);
        var windows = PortSnapshotParser.DiagnosticSession(row, windows: true);
        Assert.Equal("cmd.exe", windows.Shell);
        Assert.StartsWith("/d /s /c \"", windows.Arguments, StringComparison.Ordinal);
        Assert.Contains("8080", windows.Arguments, StringComparison.Ordinal);
        Assert.Contains("4242", windows.Arguments, StringComparison.Ordinal);
        var unix = PortSnapshotParser.DiagnosticSession(new("TCP", "127.0.0.1", 8080, "0.0.0.0:0", 4242, "o'neil", false), windows: false);
        Assert.Equal("sh", unix.Shell);
        Assert.StartsWith("-c '", unix.Arguments, StringComparison.Ordinal);
        Assert.Contains("'\"'\"'", unix.Arguments, StringComparison.Ordinal);
    }

    private static T Named<T>(Control root, string name) where T : Control
        => root.GetLogicalDescendants().OfType<T>().First(control => control.Name == name);

    private static void Click(Control root, string name)
        => Named<Button>(root, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static PluginEntry Import(PluginManager manager, string name)
    {
        manager.Import(Path.GetDirectoryName(Directory.EnumerateFiles(
            Path.Combine(AppContext.BaseDirectory, "OfficialPlugins", name), "plugin.json", SearchOption.AllDirectories).Single())!);
        var plugin = manager.Plugins.Last();
        Assert.True(plugin.Enabled, plugin.Error);
        return plugin;
    }

    private static Control Page(PluginManager manager, PluginEntry plugin)
        => manager.Modules.Single(module => module.Owner == plugin.Manifest.Id).GetView();

    [AvaloniaFact]
    public async Task PortBoard_FilterLookupAndDiagnose_DoNotKillAnything()
    {
        using var fixture = new StageLayoutTests.StageFixture();
        await fixture.ReadyAsync();
        var manager = (PluginManager)fixture.Window.GetType().GetField("_plugins", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(fixture.Window)!;
        try
        {
            var plugin = Import(manager, "PortGuard");
            Assert.Equal("official.port-guard", plugin.Manifest.Id);
            var page = Page(manager, plugin);
            var previous = fixture.Window.Content as Control;
            fixture.Window.Content = null;
            var host = new StackPanel();
            if (previous is not null) host.Children.Add(previous);
            host.Children.Add(page);
            fixture.Window.Content = host;
            var instance = typeof(PluginEntry).GetField("Instance", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(plugin)!;
            // The imported plugin lives in its own load context, so the test assembly's PortRow is a different type.
            var rowType = instance.GetType().Assembly.GetType("TerminalHub.Official.PortGuard.PortRow")!;
            var rows = (System.Collections.IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(rowType))!;
            rows.Add(Activator.CreateInstance(rowType, "TCP", "127.0.0.1", 8080, "0.0.0.0:0", 4242, "DevServer.exe", false));
            rows.Add(Activator.CreateInstance(rowType, "UDP", "0.0.0.0", 53, "*:*", 7100, "dns.exe", true));
            instance.GetType().GetField("_snapshot", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(instance, rows);
            instance.GetType().GetMethod("Show", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(instance, null);
            var list = Named<ListBox>(page, "PortRows");
            Assert.Equal(2, list.ItemCount);
            Assert.Contains("UDP 绑定", list.Items[1]!.ToString(), StringComparison.Ordinal);

            Named<TextBox>(page, "PortFilter").Text = "devserver";
            Assert.Equal(1, list.ItemCount);
            Named<TextBox>(page, "PortFilter").Text = "";
            Assert.Equal(2, list.ItemCount);
            list.SelectedIndex = 0;
            var before = fixture.Vm.SessionCards.Count;
            Click(page, "PortDiagnose");
            var deadline = Environment.TickCount64 + 5000;
            while (fixture.Vm.SessionCards.Count <= before && Environment.TickCount64 < deadline) await Task.Delay(50);
            Assert.True(fixture.Vm.SessionCards.Count > before, "diagnose should open a one-shot session");
            var spawned = fixture.Vm.SessionCards.Last().Model;
            Assert.Equal(OperatingSystem.IsWindows() ? "cmd.exe" : "sh", spawned.Shell);
            Assert.Contains("8080", spawned.ShellArguments, StringComparison.Ordinal);
            Assert.Contains("4242", spawned.ShellArguments, StringComparison.Ordinal);
            Assert.DoesNotContain("taskkill", spawned.ShellArguments, StringComparison.OrdinalIgnoreCase);

            Named<TextBox>(page, "PortQuery").Text = "53";
            Click(page, "PortLookup");
            Assert.Contains("7100", Named<TextBlock>(page, "PortMatches").Text, StringComparison.Ordinal);

            list.SelectedIndex = 0;
            Click(page, "PortEnd");
            var dialog = Assert.Single(fixture.Window.OwnedWindows.OfType<Window>());
            Assert.Contains("整个进程", string.Join(" ", dialog.GetLogicalDescendants().OfType<TextBlock>().Select(block => block.Text)), StringComparison.Ordinal);
            dialog.GetLogicalDescendants().OfType<Button>().First(button => Equals(button.Content, "取消")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Task.Delay(50);
            Assert.Contains("已取消", Named<TextBlock>(page, "PortStatus").Text, StringComparison.Ordinal);
            Assert.Equal(before + 1, fixture.Vm.SessionCards.Count);
            Assert.DoesNotContain("taskkill", Named<TextBlock>(page, "PortStatus").Text, StringComparison.OrdinalIgnoreCase);
        }
        finally { foreach (var plugin in manager.Plugins.ToArray()) manager.Remove(plugin); }
    }
}
