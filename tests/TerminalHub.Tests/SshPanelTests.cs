using System.Runtime.InteropServices;
using TerminalHub.App.ViewModels;
using TerminalHub.Core.Ssh;
using Xunit;

namespace TerminalHub.Tests;

public class SshPanelTests
{
    [Fact]
    public void SshHost_BuildsTargetAndArgs()
    {
        var h = new SshHost { Name = "prod", User = "deploy", Host = "10.0.0.7", Port = 2222 };
        Assert.Equal("deploy@10.0.0.7", h.Target);
        Assert.Equal("-p 2222 deploy@10.0.0.7", h.SshArguments);
        Assert.Equal("ssh -p 2222 deploy@10.0.0.7", h.CommandLine);
        Assert.Equal("prod", h.DisplayName);
    }

    [Fact]
    public void SshHost_NoUser_DefaultNameIsTarget()
    {
        var h = new SshHost { Host = "example.com" };
        Assert.Equal("example.com", h.Target);
        Assert.Equal("example.com", h.DisplayName);
        Assert.Equal("-p 22 example.com", h.SshArguments);
    }

    [Fact]
    public void SshHost_TargetWithWhitespace_IsQuoted()
    {
        // A host containing spaces must reach ssh as ONE argument instead of
        // being split into a mangled remote command.
        var h = new SshHost { Host = "my host", User = "u" };
        if (OperatingSystem.IsWindows())
        {
            Assert.Equal("-p 22 \"u@my host\"", h.SshArguments);
            Assert.Equal(new[] { "ssh", "-p", "22", "u@my host" }, SplitWindows(h.CommandLine));
        }
        else
            Assert.Equal("-p 22 'u@my host'", h.SshArguments);
    }

    [Fact]
    public void SshHost_EmbeddedQuote_StaysOneArgument()
    {
        var h = new SshHost { Host = "a\"b" };
        if (OperatingSystem.IsWindows())
        {
            Assert.Equal("-p 22 \"a\\\"b\"", h.SshArguments);
            Assert.Equal(new[] { "ssh", "-p", "22", "a\"b" }, SplitWindows(h.CommandLine));
        }
        else
            Assert.Equal("-p 22 'a\"b'", h.SshArguments);
    }

    [Fact]
    public void SshHost_LeadingDash_IsSeparatedFromOptions()
    {
        var h = new SshHost { Host = "-bad" };
        Assert.Equal("-p 22 -- -bad", h.SshArguments);
        if (OperatingSystem.IsWindows())
            Assert.Equal(new[] { "ssh", "-p", "22", "--", "-bad" }, SplitWindows(h.CommandLine));
    }

    private static string[] SplitWindows(string command)
    {
        var ptr = CommandLineToArgvW(command, out var count);
        Assert.NotEqual(IntPtr.Zero, ptr);
        try
        {
            var args = new string[count];
            for (var i = 0; i < count; i++)
                args[i] = Marshal.PtrToStringUni(Marshal.ReadIntPtr(ptr, i * IntPtr.Size))!;
            return args;
        }
        finally { LocalFree(ptr); }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CommandLineToArgvW(string lpCmdLine, out int pNumArgs);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr hMem);

    private static (SshViewModel vm, List<SshHost> store, List<SshHost> connected) MakeVm(bool sshAvailable = true)
    {
        var store = new List<SshHost>();
        var connected = new List<SshHost>();
        var vm = new SshViewModel(store, connected.Add, () => { }, () => sshAvailable);
        return (vm, store, connected);
    }

    [Fact]
    public void AddOrUpdate_AddsToSharedStore()
    {
        var (vm, store, _) = MakeVm();
        vm.EditName = "devbox";
        vm.EditUser = "box";
        vm.EditHost = "192.168.1.10";
        vm.EditPort = "2200";
        vm.AddOrUpdateCommand.Execute(null);

        Assert.Single(store);
        Assert.Equal("devbox", store[0].Name);
        Assert.Equal(2200, store[0].Port);
        Assert.Single(vm.Hosts);
        Assert.Contains("已添加", vm.StatusText);
    }

    [Fact]
    public void AddOrUpdate_RejectsEmptyHost_AndBadPort()
    {
        var (vm, store, _) = MakeVm();
        vm.AddOrUpdateCommand.Execute(null);
        Assert.Empty(store);
        Assert.Contains("主机", vm.StatusText);

        vm.EditHost = "x";
        vm.EditPort = "99999";
        vm.AddOrUpdateCommand.Execute(null);
        Assert.Empty(store);
        Assert.Contains("端口", vm.StatusText);
    }

    [Fact]
    public void AddOrUpdate_SameTarget_UpdatesInPlace()
    {
        var (vm, store, _) = MakeVm();
        vm.EditHost = "a.b.c";
        vm.AddOrUpdateCommand.Execute(null);
        vm.EditPort = "2222"; // same target → update, not duplicate
        vm.AddOrUpdateCommand.Execute(null);
        Assert.Single(store);
        Assert.Equal(2222, store[0].Port);
    }

    [Fact]
    public void AddOrUpdate_DifferentUser_SameHost_IsSeparateConnection()
    {
        var (vm, store, _) = MakeVm();
        vm.EditHost = "a.b.c";
        vm.AddOrUpdateCommand.Execute(null);
        vm.EditUser = "root"; // root@a.b.c != a.b.c → separate saved host
        vm.AddOrUpdateCommand.Execute(null);
        Assert.Equal(2, store.Count);
        Assert.Equal("root@a.b.c", store[1].Target);
    }

    [Fact]
    public void Remove_DeletesFromStore()
    {
        var (vm, store, _) = MakeVm();
        vm.EditHost = "gone";
        vm.AddOrUpdateCommand.Execute(null);
        vm.RemoveCommand.Execute(vm.Hosts[0]);
        Assert.Empty(store);
        Assert.Empty(vm.Hosts);
    }

    [Fact]
    public void Connect_InvokesCallback_WhenSshPresent()
    {
        var (vm, _, connected) = MakeVm(sshAvailable: true);
        vm.EditHost = "h1";
        vm.AddOrUpdateCommand.Execute(null);
        vm.ConnectCommand.Execute(vm.Hosts[0]);
        Assert.Single(connected);
        Assert.Contains("ssh -p", vm.StatusText);
    }

    [Fact]
    public void Connect_Blocked_WhenSshMissing()
    {
        var (vm, _, connected) = MakeVm(sshAvailable: false);
        vm.EditHost = "h1";
        vm.AddOrUpdateCommand.Execute(null);
        vm.ConnectCommand.Execute(vm.Hosts[0]);
        Assert.Empty(connected);
        Assert.Contains("未检测到 ssh", vm.StatusText);
    }

    [Fact]
    public void SelectingHost_FillsForm()
    {
        var (vm, _, _) = MakeVm();
        vm.EditName = "n"; vm.EditUser = "u"; vm.EditHost = "h"; vm.EditPort = "2201";
        vm.AddOrUpdateCommand.Execute(null);
        vm.Selected = vm.Hosts[0];
        Assert.Equal("n", vm.EditName);
        Assert.Equal("u", vm.EditUser);
        Assert.Equal("h", vm.EditHost);
        Assert.Equal("2201", vm.EditPort);
    }

    [Fact]
    public void Editing_ListFirst_WhenHostsExist_FormOpenWhenEmpty()
    {
        // With saved hosts the panel is a connection list, not a form.
        var store = new List<SshHost> { new SshHost { Host = "h1" } };
        var vm = new SshViewModel(store, _ => { }, () => { });
        Assert.False(vm.Editing);

        var (empty, _, _) = MakeVm();
        Assert.True(empty.Editing);
    }

    [Fact]
    public void SelectingHost_ReopensForm()
    {
        var store = new List<SshHost> { new SshHost { Host = "h1", Port = 2201 } };
        var vm = new SshViewModel(store, _ => { }, () => { });
        Assert.False(vm.Editing);
        vm.Selected = vm.Hosts[0];
        Assert.True(vm.Editing);
        Assert.Equal("2201", vm.EditPort);
        vm.ToggleEditingCommand.Execute(null);
        Assert.False(vm.Editing);
    }

    [Fact]
    public void Remove_LastSelectedHost_ReopensForm_NoDeadEnd()
    {
        // Selected row + collapsed form + delete → the empty panel must still
        // offer the form (the ＋ header hides when no hosts exist).
        var store = new List<SshHost> { new SshHost { Host = "h1" } };
        var vm = new SshViewModel(store, _ => { }, () => { });
        vm.Selected = vm.Hosts[0];
        vm.ToggleEditingCommand.Execute(null);
        Assert.False(vm.Editing);

        vm.RemoveCommand.Execute(vm.Hosts[0]);
        Assert.Empty(vm.Hosts);
        Assert.True(vm.Editing);
        Assert.Equal("", vm.EditHost);
    }

    [Fact]
    public void Remove_AfterNoOpSave_StillClearsForm()
    {
        // SshHost is a record: a no-op 添加/更新 swaps in a value-equal new
        // instance; [ObservableProperty] skips assigning an "equal" Selected, so
        // a reference compare would miss the stale selection.
        var store = new List<SshHost> { new SshHost { Host = "h1", User = "u" } };
        var vm = new SshViewModel(store, _ => { }, () => { });
        vm.Selected = vm.Hosts[0];              // form fills with u@h1
        vm.AddOrUpdateCommand.Execute(null);     // no-op save replaces the instance
        Assert.NotSame(store[0], vm.Selected);   // stale equal instance retained

        vm.RemoveCommand.Execute(vm.Hosts[0]);
        Assert.Empty(vm.Hosts);
        Assert.Null(vm.Selected);
        Assert.Equal("", vm.EditHost);           // deleted host must not linger
        Assert.True(vm.Editing);
    }

    [Fact]
    public void Remove_SelectedHost_ClearsForm()
    {
        var store = new List<SshHost> { new SshHost { Host = "h1" }, new SshHost { Host = "h2" } };
        var vm = new SshViewModel(store, _ => { }, () => { });
        vm.Selected = vm.Hosts[0];
        vm.RemoveCommand.Execute(vm.Hosts[0]);
        Assert.Single(vm.Hosts);
        Assert.Null(vm.Selected);
        Assert.Equal("", vm.EditHost);
        Assert.False(vm.Editing);
    }

    [Fact]
    public void ToggleEditing_OpensBlankNewEntry_NotSelectedHost()
    {
        var store = new List<SshHost> { new SshHost { Host = "h1", Port = 2201 } };
        var vm = new SshViewModel(store, _ => { }, () => { });
        vm.Selected = vm.Hosts[0]; // form filled with h1
        vm.ToggleEditingCommand.Execute(null);  // close
        vm.ToggleEditingCommand.Execute(null);  // ＋ = new connection

        Assert.True(vm.Editing);
        Assert.Null(vm.Selected);
        Assert.Equal("", vm.EditHost);

        vm.EditHost = "h2";
        vm.AddOrUpdateCommand.Execute(null);
        Assert.Equal(2, store.Count); // adds — must not overwrite h1
    }
}
