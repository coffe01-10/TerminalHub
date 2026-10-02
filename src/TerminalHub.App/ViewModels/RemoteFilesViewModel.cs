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
    private SftpClient? _client;
    private SshHost? _connected;
    private CancellationTokenSource? _operation;
    private bool _disposed;
    partial void OnHostChanged(SshHost? value)
    { _operation?.Cancel(); Entries.Clear(); Selected = null; Path = "."; }
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
    [RelayCommand] public Task Refresh() => Execute(async (client, ct) =>
    {
        var path = await client.RealPathAsync(Path, ct); var files = await client.ListAsync(path, ct);
        Path = path; Entries.Clear(); foreach (var f in files) Entries.Add(f); Status = $"{files.Count} 个条目";
    });
    [RelayCommand] private async Task Up() { Path = SftpClient.Join(Path, ".."); await Refresh(); }
    [RelayCommand] public async Task OpenDirectory()
    { if (Selected is { IsDirectory: true } file) { Path = file.Path; await Refresh(); } }
    [RelayCommand] private void Cancel() => _operation?.Cancel();
    [RelayCommand] private Task OpenTerminal() => Host is { } host ? openTerminal(host, Path) : Task.CompletedTask;
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
