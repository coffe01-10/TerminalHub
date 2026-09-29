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
        Assert.Equal("-p 22 \"u@my host\"", h.SshArguments);
    }

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
}
