using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TerminalHub.Core.Ssh;

namespace TerminalHub.App.ViewModels;

/// <summary>
/// Right-rail SSH tab: editable saved-host list + connect via the local
/// `ssh` binary (password/key interaction happens inside the terminal).
/// </summary>
public partial class SshViewModel : ViewModelBase
{
    private readonly List<SshHost> _hosts; // == AppSettings.SshHosts (persisted list)
    private readonly Action _persist;
    private readonly Action<SshHost> _connect;
    private readonly Func<bool> _sshAvailable;

    public ObservableCollection<SshHost> Hosts { get; } = [];

    [ObservableProperty] private string _editName = "";
    [ObservableProperty] private string _editUser = "";
    [ObservableProperty] private string _editHost = "";
    [ObservableProperty] private string _editPort = "22";
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private SshHost? _selected;
    /// <summary>Whether the edit form is expanded. Saved hosts get a list-first
    /// view; the form opens on demand (or automatically while no host exists).</summary>
    [ObservableProperty] private bool _editing;
    public bool HasHosts => Hosts.Count > 0;

    public SshViewModel(
        List<SshHost> hosts,
        Action<SshHost> connect,
        Action persist,
        Func<bool>? sshAvailable = null)
    {
        _hosts = hosts;
        _connect = connect;
        _persist = persist;
        _sshAvailable = sshAvailable ?? SshLocator.Available;
        foreach (var h in _hosts) Hosts.Add(h);
        Hosts.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasHosts));
        _editing = Hosts.Count == 0;
    }

    /// <summary>Selecting a row fills the edit form (edit → 添加/更新 to save).</summary>
    partial void OnSelectedChanged(SshHost? value)
    {
        if (value is null) return;
        EditName = value.Name;
        EditUser = value.User;
        EditHost = value.Host;
        EditPort = value.Port.ToString();
        Editing = true; // selecting a row means the user wants to edit it
    }

    /// <summary>Host list is the default view once hosts exist — ＋ opens a
    /// blank form for a new connection (never pre-filled with the selected
    /// host, or 添加/更新 would overwrite that row instead of adding).</summary>
    [RelayCommand]
    private void ToggleEditing()
    {
        if (Editing) { Editing = false; return; }
        Selected = null;
        ClearEdit();
        Editing = true;
    }

    private void ClearEdit()
    {
        EditName = EditUser = EditHost = "";
        EditPort = "22";
    }

    /// <summary>Add a new host, or update the row whose Name/Target matches.</summary>
    [RelayCommand]
    private void AddOrUpdate()
    {
        var host = EditHost.Trim();
        if (string.IsNullOrEmpty(host))
        {
            StatusText = "主机地址不能为空";
            return;
        }
        if (!int.TryParse(EditPort.Trim(), out var port) || port is < 1 or > 65535)
        {
            StatusText = "端口无效 (1-65535)";
            return;
        }

        var entry = new SshHost
        {
            Name = EditName.Trim(),
            User = EditUser.Trim(),
            Host = host,
            Port = port,
        };

        // Update in place when the same display name or target already exists.
        var idx = -1;
        for (var i = 0; i < Hosts.Count; i++)
        {
            var h = Hosts[i];
            if ((!string.IsNullOrEmpty(entry.Name) && h.Name == entry.Name) || h.Target == entry.Target)
            { idx = i; break; }
        }

        if (idx >= 0)
        {
            Hosts[idx] = entry;
            _hosts[idx] = entry;
        }
        else
        {
            Hosts.Add(entry);
            _hosts.Add(entry);
        }
        _persist();
        StatusText = idx >= 0 ? $"已更新 {entry.DisplayName}" : $"已添加 {entry.DisplayName}";
        Selected = entry;
    }

    [RelayCommand]
    private void Remove(SshHost? host)
    {
        if (host is null) return;
        var idx = Hosts.IndexOf(host);
        if (idx < 0) return;
        // SshHost is a record (value equality): after AddOrUpdate replaces the
        // row instance, Selected still holds the stale-but-equal record, so a
        // reference compare would miss. Value compare is what we need.
        var wasSelected = Selected is { } s && s == host;
        Hosts.RemoveAt(idx);
        _hosts.RemoveAt(idx);
        _persist();
        if (wasSelected)
        {
            // The deleted host's values must not linger in the form.
            Selected = null;
            ClearEdit();
        }
        // The empty list always offers the form — it is the only way to add.
        if (Hosts.Count == 0) Editing = true;
        else if (wasSelected) Editing = false;
    }

    /// <summary>Spawn a new terminal session running `ssh -p port user@host`.</summary>
    [RelayCommand]
    private void Connect(SshHost? host)
    {
        if (host is null) return;
        if (!_sshAvailable())
        {
            StatusText = "未检测到 ssh 命令 — 请先安装 openssh-client";
            return;
        }
        StatusText = $"连接 {host.CommandLine} …";
        _connect(host);
    }
}
