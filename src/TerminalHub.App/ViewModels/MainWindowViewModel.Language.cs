using System.Globalization;
using TerminalHub.Core.Localization;

namespace TerminalHub.App.ViewModels;

public partial class MainWindowViewModel
{
    private void RefreshUiLanguage()
    {
        OnPropertyChanged(nameof(ActiveDirectoryName)); OnPropertyChanged(nameof(LeftPaneName));
        OnPropertyChanged(nameof(RightPaneName)); OnPropertyChanged(nameof(BottomLeftPaneName));
        OnPropertyChanged(nameof(BottomRightPaneName)); OnPropertyChanged(nameof(NextSessionMenuText));
        OnPropertyChanged(nameof(PreviousSessionMenuText));
        OnPropertyChanged(nameof(LastPublishBadge)); OnPropertyChanged(nameof(DeployDockCaption));
        OnPropertyChanged(nameof(DeployDockTip)); OnPropertyChanged(nameof(ActivePublishProfileLabel));
        NotifyLayoutHistory(); UpdateGroupActivity(); RefreshTemplatePreview();
        Logs.RefreshLanguage();
    }
    public int LanguageIndex
    {
        get => _settings.Language switch { "zh-CN" => 1, "en" => 2, _ => 0 };
        set
        {
            _settings.Language = value switch { 1 => "zh-CN", 2 => "en", _ => "system" };
            Localizer.Current.SetLanguage(_settings.Language);
            OnPropertyChanged(); SaveSettingsInternal();
        }
    }
}
