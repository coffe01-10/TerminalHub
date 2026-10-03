using System.Text;
using System.Text.Json;

namespace TerminalHub.Core.Settings;

public static class WorkspaceTemplateTransfer
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static string ToJson(WorkspaceTemplate template) => JsonSerializer.Serialize(template, Json);

    public static bool TryParse(string json, out WorkspaceTemplate? template, out string error)
    {
        template = null;
        error = "";
        try
        {
            var parsed = JsonSerializer.Deserialize<WorkspaceTemplate>(json, Json);
            if (parsed is null || parsed.Layout.Sessions.Count == 0)
            {
                error = "模板里没有终端。";
                return false;
            }
            if (string.IsNullOrWhiteSpace(parsed.Name)) parsed.Name = "导入的模板";
            parsed.Id = Guid.NewGuid().ToString("N");
            template = parsed;
            return true;
        }
        catch (Exception)
        {
            error = "无法读取这个模板文件。";
            return false;
        }
    }

    public static WorkspaceTemplate Clone(WorkspaceTemplate source)
    {
        if (!TryParse(ToJson(source), out var copy, out _) || copy is null)
            throw new InvalidOperationException("模板复制失败。");
        copy.Name = source.Name.Trim() + " 副本";
        copy.LastUsed = source.LastUsed;
        return copy;
    }

    public static string Describe(WorkspaceTemplate template, Func<string, bool>? directoryExists = null, Func<string, bool>? shellExists = null)
    {
        var text = new StringBuilder();
        text.Append(TerminalHub.Core.Localization.Localizer.Current.Translate("将创建 ")).Append(template.Layout.Sessions.Count).AppendLine(TerminalHub.Core.Localization.Localizer.Current.Translate(" 个终端："));
        foreach (var session in template.Layout.Sessions)
        {
            var directory = string.IsNullOrWhiteSpace(session.WorkingDirectory) ? TerminalHub.Core.Localization.Localizer.Current.Translate("默认目录") : session.WorkingDirectory;
            var directoryNote = directoryExists is not null && !string.IsNullOrWhiteSpace(session.WorkingDirectory)
                && !directoryExists(session.WorkingDirectory) ? TerminalHub.Core.Localization.Localizer.Current.Translate("（目录不存在，可修改）") : "";
            var shell = string.IsNullOrWhiteSpace(session.Shell) ? TerminalHub.Core.Localization.Localizer.Current.Translate("默认 Shell") : session.Shell;
            var shellNote = shellExists is not null && !string.IsNullOrWhiteSpace(session.Shell)
                && !shellExists(session.Shell) ? TerminalHub.Core.Localization.Localizer.Current.Translate("（未找到 Shell，可修改）") : "";
            text.Append(session.Name).Append(" · ").Append(directory).Append(directoryNote)
                .Append(" · ").Append(shell).Append(shellNote);
            if (session.RunStartupCommand && !string.IsNullOrWhiteSpace(session.StartupCommand))
                text.Append(TerminalHub.Core.Localization.Localizer.Current.Translate(" · 启动命令：")).Append(session.StartupCommand.Replace("\r", "").Replace('\n', ' '));
            if (session.Pinned) text.Append(TerminalHub.Core.Localization.Localizer.Current.Translate(" · 置顶"));
            if (!string.IsNullOrWhiteSpace(session.GroupName)) text.Append(TerminalHub.Core.Localization.Localizer.Current.Translate(" · 分组 ")).Append(session.GroupName);
            text.AppendLine();
        }
        return text.ToString().TrimEnd();
    }
}
