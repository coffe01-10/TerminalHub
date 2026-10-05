using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TerminalHub.Core.Ssh;

namespace TerminalHub.App.ViewModels;

public partial class RemoteFilesViewModel(Func<SshHost, string, Task> openTerminal) : ViewModelBase, IDisposable
{
    public ObservableCollection<RemoteFile> Entries { get; } = [];
    [ObservableProperty] private SshHost? _host;
    [ObservableProperty] private string _path = ".";
    [ObservableProperty] private RemoteFile? _selected;
    [ObservableProperty] private string _status = "选择已保存的 SSH 主机。文件传输使用本机 OpenSSH 密钥或代理认证。";
    [ObservableProperty] private bool _busy;
    [ObservableProperty] private double _progress;
    /// <summary>Name box: target of 新建文件夹 / 重命名. Selecting an entry
    /// fills it (rename起点); clearing it defaults a new folder to 新建文件夹.</summary>
    [ObservableProperty] private string _nameDraft = "";
    private SftpClient? _client;
    private SshHost? _connected;
    private CancellationTokenSource? _operation;
    private RemoteFile? _pendingDelete;
    private bool _disposed;
    partial void OnHostChanged(SshHost? value)
    { _operation?.Cancel(); Entries.Clear(); Selected = null; Path = "."; }
    partial void OnSelectedChanged(RemoteFile? value)
    { _pendingDelete = null; if (value is { } f) NameDraft = f.Name; }
    private async Task Execute(Func<SftpClient, CancellationToken, Task> action)
    {
        if (Busy || Host is null || _disposed) return;
        Busy = true; Progress = 0; _operation = new();
        try
        {
            if (_client is null || _connected != Host)
            { _client?.Dispose(); _client = null; _client = await SftpClient.ConnectAsync(Host, _operation.Token); _connected = Host; }
            await action(_client, _operation.Token);
        }
        catch (OperationCanceledException) { Status = "操作已取消。" + IncompleteTransferNotice(); _client?.Dispose(); _client = null; }
        catch (Exception ex) { Status = ex.Message + IncompleteTransferNotice(); _client?.Dispose(); _client = null; }
        finally { _operation.Dispose(); _operation = null; Busy = false; }
    }
    private string IncompleteTransferNotice() => _client?.IncompleteRemotePath is { } path ? " 未确认删除远端临时文件：" + path : "";
    [RelayCommand] public Task Refresh() => Execute(RefreshEntries);
    /// <summary>List reload for both the public Refresh command and commands
    /// mid-Execute — Refresh() itself would no-op while Busy.</summary>
    private async Task RefreshEntries(SftpClient client, CancellationToken ct)
    {
        var path = await client.RealPathAsync(Path, ct); var files = await client.ListAsync(path, ct);
        Path = path; Entries.Clear(); foreach (var f in files) Entries.Add(f); Status = $"{files.Count} 个条目";
    }
    [RelayCommand] private async Task Up() { Path = SftpClient.Join(Path, ".."); await Refresh(); }
    [RelayCommand] public async Task OpenDirectory()
    { if (Selected is { IsDirectory: true } file) { Path = file.Path; await Refresh(); } }
    [RelayCommand] private void Cancel() => _operation?.Cancel();
    [RelayCommand] private Task OpenTerminal() => Host is { } host ? openTerminal(host, Path) : Task.CompletedTask;
    /// <summary>Create a remote directory from the name box (auto-suffixes a
    /// number when the name already exists), then refresh.</summary>
    [RelayCommand] private Task NewFolder() => Execute(async (client, ct) =>
    {
        var name = string.IsNullOrWhiteSpace(NameDraft) ? "新建文件夹" : NameDraft.Trim();
        var parent = await client.RealPathAsync(Path, ct);
        var target = SftpClient.Join(parent, name);
        for (var i = 2; ; i++)
        {
            try { await client.StatAsync(target, ct); target = $"{SftpClient.Join(parent, name)} {i}"; }
            catch (IOException) { break; } // no such file — the name is free
        }
        await client.MkdirAsync(target, ct);
        await RefreshEntries(client, ct);
        Status = $"已创建 {target[target.LastIndexOf('/') ..]}";
    });
    /// <summary>Rename the selected entry to the name box's value.</summary>
    [RelayCommand] private Task Rename() => Execute(async (client, ct) =>
    {
        var file = Selected;
        if (file is null) { Status = "先选择要重命名的条目"; return; }
        if (string.IsNullOrWhiteSpace(NameDraft) || NameDraft.Trim() == file.Name)
        { Status = "名称未变化"; return; }
        var parent = file.Path[..^file.Name.Length].TrimEnd('/');
        await client.RenameAsync(file.Path, SftpClient.Join(parent, NameDraft.Trim()), ct);
        await RefreshEntries(client, ct);
        Status = $"已重命名 {file.Name} → {NameDraft.Trim()}";
    });
    /// <summary>Delete the selected entry — file via SSH_FXP_REMOVE, empty
    /// directory via SSH_FXP_RMDIR. First click arms the delete, a second
    /// click on the same selection commits it (irreversible op).</summary>
    [RelayCommand] private async Task Delete()
    {
        var file = Selected;
        if (file is null) { Status = "先选择要删除的条目"; return; }
        if (_pendingDelete != file)
        {
            _pendingDelete = file;
            Status = $"再次点击“删除”确认删除 {(file.IsDirectory ? "文件夹" : "文件")} {file.Name}";
            return;
        }
        _pendingDelete = null;
        await Execute(async (client, ct) =>
        {
            await client.RemoveAsync(file, ct);
            Selected = null;
            await RefreshEntries(client, ct);
            Status = $"已删除 {file.Name}";
        });
    }
    public async Task UploadAsync(string local)
    {
        var uploaded = false;
        await Execute(async (client, ct) =>
        {
            var total = new FileInfo(local).Length; var remote = SftpClient.Join(await client.RealPathAsync(Path, ct), System.IO.Path.GetFileName(local));
            await client.UploadAsync(local, remote, new Progress<long>(n => { if (!Busy) return; Progress = total == 0 ? 100 : n * 100d / total; Status = $"上传 {n:N0} / {total:N0} 字节"; }), ct);
            uploaded = true;
            Status = "上传完成";
        });
        if (uploaded && _client is not null) { await Refresh(); Status = "上传完成 · " + Entries.Count + " 个条目"; }
    }
    public Task DownloadAsync(string local)
    {
        var file = Selected;
        if (file is null || file.IsDirectory) return Task.CompletedTask;
        return Execute(async (client, ct) =>
        {
            await client.DownloadAsync(file, local, new Progress<long>(n => { if (!Busy) return; Progress = file.Size == 0 ? 100 : n * 100d / file.Size; Status = $"下载 {n:N0} / {file.Size:N0} 字节"; }), ct);
            Status = "下载完成";
        });
    }
    public void Dispose() { _disposed = true; _operation?.Cancel(); _client?.Dispose(); _client = null; }
}
