using System.Collections.ObjectModel;
using Avalonia.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TerminalHub.Core.Settings;

namespace TerminalHub.App.ViewModels;

public partial class MainWindowViewModel
{
    public ObservableCollection<FavoriteCommandViewModel> FavoriteCommands { get; } = [];
    [ObservableProperty] private string _favoriteNotice = "";

    private void LoadFavorites()
    {
        foreach (var favorite in _settings.FavoriteCommands)
            FavoriteCommands.Add(new FavoriteCommandViewModel(favorite, ValidateSessionShortcuts));
        ValidateSessionShortcuts();
    }

    [RelayCommand]
    private void AddFavorite()
    {
        var model = new FavoriteCommand { Name = "新命令", Command = "" };
        _settings.FavoriteCommands.Add(model);
        FavoriteCommands.Add(new FavoriteCommandViewModel(model, ValidateSessionShortcuts));
        FavoriteNotice = "已添加收藏。填写命令后，从命令面板或快捷键填入当前终端。";
        SaveSettingsInternal();
    }

    [RelayCommand]
    private void DeleteFavorite(FavoriteCommandViewModel? favorite)
    {
        if (favorite is null) return;
        FavoriteCommands.Remove(favorite);
        _settings.FavoriteCommands.Remove(favorite.Model);
        ValidateSessionShortcuts();
        FavoriteNotice = "已删除收藏。";
        SaveSettingsInternal();
    }

    public void InsertFavorite(FavoriteCommand favorite)
    {
        if (ActiveSession is null) { FavoriteNotice = "先打开一个终端。"; return; }
        var command = (favorite.Command ?? "").Replace("\r\n", "\n").Replace('\r', '\n');
        if (command.Contains('\n') && !ActiveSession.Emulator.Buffer.BracketedPaste)
        {
            FavoriteNotice = "这个终端没有开启括号粘贴，多行命令会逐行执行。本次没有填入。";
            return;
        }
        ActiveSession.Emulator.PasteText(favorite.Command ?? "");
        FavoriteNotice = FavoriteCommand.AppliesTo(favorite.Shell, ActiveSession.Shell)
            ? "已填入收藏命令，确认后按 Enter 执行。"
            : "当前 Shell 与收藏记录的不同，已按原文填入。确认后按 Enter 执行。";
    }

    private void ValidateFavoriteGestures()
    {
        foreach (var favorite in FavoriteCommands)
        {
            favorite.Error = "";
            favorite.ParsedGesture = null;
            if (string.IsNullOrWhiteSpace(favorite.Shortcut)) continue;
            try
            {
                var gesture = ParseSessionGesture(favorite.Shortcut);
                if ((gesture.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Meta)) == 0
                    && gesture.Key is not (>= Key.F1 and <= Key.F24))
                    favorite.Error = "请使用 Ctrl、Alt 或功能键，避免占用普通输入。";
                else if (IsReservedShortcut(gesture))
                    favorite.Error = "与复制、粘贴或现有应用操作冲突。";
                else favorite.ParsedGesture = gesture;
            }
            catch (Exception ex) when (ex is FormatException or ArgumentException)
            { favorite.Error = "无法识别这个快捷键，请重新按键录入。"; }
        }
        var used = SessionShortcuts.Where(item => item.ParsedGesture is not null && item.Error.Length == 0)
            .Select(item => item.ParsedGesture!)
            .Concat(FavoriteCommands.Where(item => item.ParsedGesture is not null && item.Error.Length == 0).Select(item => item.ParsedGesture!))
            .GroupBy(gesture => (gesture.Key, gesture.KeyModifiers))
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet();
        foreach (var favorite in FavoriteCommands)
        {
            if (favorite.ParsedGesture is { } gesture && used.Contains((gesture.Key, gesture.KeyModifiers)))
                favorite.Error = "与其他快捷键重复。";
        }
    }
}
