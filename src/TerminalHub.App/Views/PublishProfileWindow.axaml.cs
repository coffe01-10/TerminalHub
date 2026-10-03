using Avalonia.Controls;
using Avalonia.Interactivity;
using TerminalHub.Core.Deploy;

namespace TerminalHub.App.Views;

/// <summary>Small Deploy-dock dialog that names the profile written to settings.json.</summary>
public partial class PublishProfileWindow : Window
{
    public PublishProfileWindow()
    {
        InitializeComponent();
        Opened += (_, _) => NameBox.Focus();
    }

    public string ProfileName => NameBox.Text?.Trim() ?? "";
    public string RepoRoot => RootBox.Text?.Trim() ?? "";
    public string NoteText => NoteBox.Text?.Trim() ?? "";

    public string SelectedRid => RidBox.SelectedIndex switch
    {
        1 => PublishProfiles.LinuxRid,
        2 => PublishProfiles.WindowsRid,
        _ => "",
    };

    public void ApplyDraft(PublishProfileDraft draft)
    {
        NameBox.Text = draft.Name;
        RootBox.Text = draft.RepoRoot;
        NoteBox.Text = draft.Note;
        RidBox.SelectedIndex = draft.Rid switch
        {
            PublishProfiles.LinuxRid => 1,
            PublishProfiles.WindowsRid => 2,
            _ => 0,
        };
    }

    private void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NameBox.Text))
        {
            TerminalHub.App.Localization.UiText.Set(ErrorText, Avalonia.Controls.TextBlock.TextProperty, "名称不能为空");
            ErrorText.IsVisible = true;
            return;
        }
        Close(true);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(false);
}
