using System.Collections.ObjectModel;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.Threading;
using TerminalHub.App.ViewModels;
using TerminalHub.Core.Localization;
using TerminalHub.Core.Settings;
using TerminalHub.Extensibility;

namespace TerminalHub.App.Plugins;

public sealed class ModuleRegistration(string owner, ModuleDefinition definition, Func<Control> factory)
{
    public string Owner { get; } = owner;
    public ModuleDefinition Definition { get; } = definition;
    public string Id => Owner + ":" + Definition.Id;
    public Control? View { get; private set; }
    public Control GetView() => View ??= factory();
    public override string ToString() => Localizer.Current.GetModuleText(Owner, Definition.Id, Localizer.Current.Translate(Definition.Title));
    public void Release() { if (View is IDisposable disposable) disposable.Dispose(); View = null; }
}

public sealed class PluginEntry(PluginManifest manifest, string directory)
{
    public PluginManifest Manifest { get; } = manifest;
    public string Directory { get; } = directory;
    public string Error { get; internal set; } = "";
    public bool Enabled => Context is not null;
    internal PluginContext? Context;
    internal IWorkbenchPlugin? Instance;
    internal AssemblyLoadContext? Loader;
    internal bool Stopping;
}

public sealed class PluginManager : IDisposable
{
    public const int ApiVersion = 1;
    private readonly MainWindowViewModel _vm;
    private readonly Styles _styleTarget;
    private readonly DispatcherTimer _outputTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    public string DirectoryPath { get; }
    public ObservableCollection<PluginEntry> Plugins { get; } = [];
    public ObservableCollection<ModuleRegistration> Modules { get; } = [];
    public ObservableCollection<(string Owner, PluginCommand Command)> Commands { get; } = [];
    public event Action? Changed;
    public string LastError { get; private set; } = "";
    internal event Action<WorkbenchEvent>? Event;
    private bool _disposed;

    public PluginManager(MainWindowViewModel vm, string? directory = null, Styles? styles = null)
    {
        _styleTarget = styles ?? Application.Current!.Styles;
        _vm = vm; DirectoryPath = directory ?? Path.Combine(Path.GetDirectoryName(SettingsStore.DefaultPath())!, "plugins");
        _vm.WorkbenchChanged += Publish;
        _outputTimer.Tick += (_, _) => _vm.FlushPluginOutput(); _outputTimer.Start();
        Localizer.Current.LanguageChanged += LanguageChanged;
        Dispatcher.UIThread.UnhandledException += OnPluginException;
    }
    public void Discover()
    {
        if (!Directory.Exists(DirectoryPath)) return;
        foreach (var directory in Directory.GetDirectories(DirectoryPath))
        {
            var previous = Plugins.FirstOrDefault(p => p.Directory == directory);
            if (previous?.Enabled == true) continue;
            if (previous is not null) Plugins.Remove(previous);
            try
            {
                if (!File.Exists(Path.Combine(directory, "plugin.json"))) continue;
                var manifest = JsonSerializer.Deserialize<PluginManifest>(File.ReadAllText(Path.Combine(directory,"plugin.json")),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? throw new InvalidDataException("plugin.json 为空");
                if (string.IsNullOrWhiteSpace(manifest.Id)) throw new InvalidDataException("plugin.json 缺少 id");
                if (Plugins.Any(p => p.Manifest.Id == manifest.Id)) throw new InvalidDataException("插件标识重复：" + manifest.Id);
                var plugin = new PluginEntry(manifest, directory); Plugins.Add(plugin);
                if (_vm.PluginPreferences.GetValueOrDefault(manifest.Id)?.Enabled == true) Enable(plugin);
            }
            catch (Exception ex)
            {
                var name = Path.GetFileName(directory);
                LastError = name + ": " + ex.Message;
                Plugins.Add(new(new() { Id = name, Name = name }, directory) { Error = ex.Message });
            }
        }
        Refresh();
    }
    public void Import(string source)
    {
        var manifest = JsonSerializer.Deserialize<PluginManifest>(File.ReadAllText(Path.Combine(source,"plugin.json")),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? throw new InvalidDataException("plugin.json 为空");
        // The manifest ID names an installed directory; it must not escape the plugin root.
        if (string.IsNullOrWhiteSpace(manifest.Id) || manifest.Id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || manifest.Id is "." or "..")
            throw new InvalidDataException("插件 id 必须是可用的目录名称");
        var target = Path.GetFullPath(Path.Combine(DirectoryPath, manifest.Id));
        var origin = Path.GetFullPath(source);
        if (origin.Equals(target, StringComparison.OrdinalIgnoreCase)) { Discover(); return; }
        if (target.StartsWith(origin + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("请选择独立的插件构建目录");
        var existing = Plugins.FirstOrDefault(p => p.Manifest.Id == manifest.Id);
        if (existing is not null) { Disable(existing); Plugins.Remove(existing); }
        Directory.CreateDirectory(target);
        foreach (var file in Directory.GetFiles(origin,"*",SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(origin,file);
            if (Path.GetFileName(file).Equals("settings.json", StringComparison.OrdinalIgnoreCase)
                || Path.GetFileName(file).StartsWith("settings-", StringComparison.OrdinalIgnoreCase)
                || relative.Split(Path.DirectorySeparatorChar).Contains(".ssh")) continue;
            var destination = Path.Combine(target,relative); Directory.CreateDirectory(Path.GetDirectoryName(destination)!); File.Copy(file,destination,true);
        }
        Discover(); if (Plugins.FirstOrDefault(p => p.Manifest.Id == manifest.Id) is { } imported) Enable(imported);
    }
    public void Enable(PluginEntry plugin)
    {
        if (plugin.Enabled || _disposed) return;
        try
        {
            if (plugin.Manifest.HostApi != ApiVersion) throw new NotSupportedException($"宿主 API {plugin.Manifest.HostApi} 不受支持；当前为 {ApiVersion}");
            var entry = Path.GetFullPath(Path.Combine(plugin.Directory,plugin.Manifest.Entry));
            if (!entry.StartsWith(Path.GetFullPath(plugin.Directory) + Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("插件入口必须位于插件目录中");
            var loader = new PluginLoadContext(entry);
            plugin.Loader = loader;
            var assembly = loader.ReadAssembly(entry);
            var type = string.IsNullOrWhiteSpace(plugin.Manifest.EntryType)
                ? assembly.GetTypes().First(t => typeof(IWorkbenchPlugin).IsAssignableFrom(t) && !t.IsAbstract)
                : assembly.GetType(plugin.Manifest.EntryType, true)!;
            plugin.Instance = (IWorkbenchPlugin)Activator.CreateInstance(type)!;
            plugin.Context = new(this,plugin,_vm);
            plugin.Instance.Initialize(plugin.Context);
            plugin.Error = "";
            if (LastError.StartsWith(plugin.Manifest.Name + ": ",StringComparison.Ordinal)) LastError = "";
            Preferences(plugin).Enabled = true; _vm.SavePluginPreferences(); Refresh();
        }
        catch (Exception ex) { Fail(plugin,ex); }
    }
    public void Disable(PluginEntry plugin)
    {
        if (plugin.Stopping) return;
        plugin.Stopping = true;
        try
        {
            plugin.Context?.Cancel();
            try { plugin.Instance?.Deactivate(); } catch (Exception ex) { plugin.Error = ex.GetBaseException().Message; }
            plugin.Context?.Dispose(); plugin.Context = null; plugin.Instance = null;
            plugin.Loader?.Unload(); plugin.Loader = null;
            Preferences(plugin).Enabled = false; _vm.SavePluginPreferences(); Refresh();
        }
        finally { plugin.Stopping = false; }
    }
    public void Remove(PluginEntry plugin)
    {
        Disable(plugin);
        var root = Path.GetFullPath(DirectoryPath) + Path.DirectorySeparatorChar;
        var target = Path.GetFullPath(plugin.Directory);
        if (!target.StartsWith(root,StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("插件目录不在安装目录中");
        Directory.Delete(target,true); Plugins.Remove(plugin); _vm.PluginPreferences.Remove(plugin.Manifest.Id); _vm.SavePluginPreferences(); Refresh();
    }
    private PluginSettings Preferences(PluginEntry plugin)
    {
        if (!_vm.PluginPreferences.TryGetValue(plugin.Manifest.Id,out var state)) _vm.PluginPreferences[plugin.Manifest.Id] = state = new();
        return state;
    }
    public ModuleSettings Settings(ModuleRegistration module)
    {
        if (!_vm.ModulePreferences.TryGetValue(module.Id,out var state)) _vm.ModulePreferences[module.Id] = state = new();
        return state;
    }
    public IEnumerable<ModuleRegistration> Visible(ExtensionSurface surface) => Modules.Where(m => m.Definition.Surface == surface
        && Settings(m).Visible && Settings(m).Workspaces.GetValueOrDefault(_vm.ActiveWorkspace.Id,true))
        .OrderBy(m => Settings(m).Order ?? m.Definition.Order);
    public IDisposable RegisterBuiltin(ModuleDefinition definition, Func<Control> factory) => AddModule("builtin",definition,factory);
    internal IDisposable AddModule(string owner, ModuleDefinition definition, Func<Control> factory)
    {
        if (Modules.Any(m => m.Owner == owner && m.Definition.Id == definition.Id)) throw new InvalidOperationException("模块标识重复：" + definition.Id);
        var module = new ModuleRegistration(owner,definition,factory); Modules.Add(module); Refresh();
        return new Cleanup(() => { Modules.Remove(module); Refresh(); module.Release(); });
    }
    internal void Refresh() => Changed?.Invoke();
    internal IDisposable AddStyle(IStyle style)
    { _styleTarget.Add(style); return new Cleanup(() => _styleTarget.Remove(style)); }
    public void SaveModules() { _vm.SavePluginPreferences(); Refresh(); }
    internal void Fail(PluginEntry plugin, Exception exception)
    {
        plugin.Error = exception.GetBaseException().Message;
        LastError = plugin.Manifest.Name + ": " + plugin.Error;
        Disable(plugin); Refresh();
    }
    private void OnPluginException(object? sender, DispatcherUnhandledExceptionEventArgs e)
    {
        var assemblies = new System.Diagnostics.StackTrace(e.Exception).GetFrames()
            .Select(f => f.GetMethod()?.DeclaringType?.Assembly).ToHashSet();
        var plugin = Plugins.FirstOrDefault(p => p.Enabled && p.Instance is not null && assemblies.Contains(p.Instance.GetType().Assembly));
        if (plugin is null) return;
        e.Handled = true; Fail(plugin,e.Exception);
    }
    private void Publish(WorkbenchEvent notification)
    {
        foreach (var callback in Event?.GetInvocationList().Cast<Action<WorkbenchEvent>>() ?? []) callback(notification);
        if (notification.Kind == WorkbenchEventKind.WorkspaceChanged) Refresh();
    }
    private void LanguageChanged() { Publish(new(WorkbenchEventKind.LanguageChanged)); Refresh(); }
    public bool HandleShortcut(KeyEventArgs e)
    {
        if (e.KeyModifiers == KeyModifiers.None) return false;
        foreach (var (_,command) in Commands.ToArray())
            if (!string.IsNullOrWhiteSpace(command.Gesture) && KeyGesture.Parse(command.Gesture).Matches(e))
            { _ = command.Execute(); return true; }
        return false;
    }
    public void Dispose()
    {
        if (_disposed) return; _disposed = true;
        _outputTimer.Stop(); _vm.WorkbenchChanged -= Publish; Localizer.Current.LanguageChanged -= LanguageChanged;
        Dispatcher.UIThread.UnhandledException -= OnPluginException;
        // Shutdown clears registrations without changing the saved enabled preference.
        foreach (var plugin in Plugins.Where(p => p.Enabled).ToArray())
        {
            var enabled = Preferences(plugin).Enabled;
            Disable(plugin); Preferences(plugin).Enabled = enabled;
        }
        _vm.SavePluginPreferences();
    }
}

internal sealed class Cleanup(Action action) : IDisposable
{ private Action? _action = action; public void Dispose() => Interlocked.Exchange(ref _action,null)?.Invoke(); }

internal sealed class PluginLoadContext(string entry) : AssemblyLoadContext(isCollectible: true)
{
    private readonly AssemblyDependencyResolver _resolver = new(entry);
    public Assembly ReadAssembly(string path)
    { using var stream = File.OpenRead(path); return LoadFromStream(stream); }
    protected override Assembly? Load(AssemblyName name)
    {
        if (name.Name is "TerminalHub.Extensibility" or "TerminalHub.Core" || name.Name?.StartsWith("Avalonia",StringComparison.Ordinal) == true)
            return Default.Assemblies.FirstOrDefault(a => a.GetName().Name == name.Name) ?? Default.LoadFromAssemblyName(name);
        var path = _resolver.ResolveAssemblyToPath(name);
        path ??= Path.Combine(Path.GetDirectoryName(entry)!,name.Name + ".dll");
        return File.Exists(path) ? ReadAssembly(path) : null;
    }
    protected override IntPtr LoadUnmanagedDll(string name)
    { var path = _resolver.ResolveUnmanagedDllToPath(name); return path is null ? IntPtr.Zero : LoadUnmanagedDllFromPath(path); }
}

internal sealed class PluginContext(PluginManager manager, PluginEntry plugin, MainWindowViewModel vm) : IPluginContext, IDisposable
{
    private readonly List<IDisposable> _resources = [];
    private readonly CancellationTokenSource _lifetime = new();
    public string PluginId => plugin.Manifest.Id;
    public string Directory => plugin.Directory;
    public IWorkbenchHost Host => vm;
    public CancellationToken Lifetime => _lifetime.IsCancellationRequested ? new(true) : _lifetime.Token;
    public string Language => Localizer.Current.Language;
    public string Text(string key,string fallback) => Localizer.Current.GetModuleText(PluginId,key,fallback);
    public T? ReadConfiguration<T>() => vm.PluginPreferences.TryGetValue(PluginId,out var state) && !string.IsNullOrEmpty(state.Configuration)
        ? JsonSerializer.Deserialize<T>(state.Configuration) : default;
    public void SaveConfiguration<T>(T value)
    {
        if (!vm.PluginPreferences.TryGetValue(PluginId,out var state)) vm.PluginPreferences[PluginId] = state = new();
        state.Configuration = JsonSerializer.Serialize(value); vm.SavePluginPreferences();
    }
    public IDisposable Track(IDisposable resource) { Lifetime.ThrowIfCancellationRequested(); _resources.Add(resource); return resource; }
    public IDisposable RegisterView(ModuleDefinition module, Func<Control> create) => Track(manager.AddModule(PluginId,module,() =>
    {
        try { return create(); }
        catch (Exception ex) { Dispatcher.UIThread.Post(() => ReportError(ex)); return new TextBlock { Text = plugin.Manifest.Name + ": " + ex.Message, TextWrapping = Avalonia.Media.TextWrapping.Wrap }; }
    }));
    public IDisposable RegisterCommand(PluginCommand command)
    {
        if (!string.IsNullOrWhiteSpace(command.Gesture)) KeyGesture.Parse(command.Gesture);
        var wrapped = command with { Execute = async () =>
        {
            try { if (!Lifetime.IsCancellationRequested) await command.Execute(); }
            catch (OperationCanceledException) when (Lifetime.IsCancellationRequested) { }
            catch (Exception ex) { ReportError(ex); }
        } };
        var item = (PluginId,wrapped); manager.Commands.Add(item); manager.Refresh();
        return Track(new Cleanup(() => { manager.Commands.Remove(item); manager.Refresh(); }));
    }
    public IDisposable RegisterStyles(IStyle style)
        => Track(manager.AddStyle(style));
    public IDisposable RegisterResources(IResourceProvider resource)
    { Application.Current!.Resources.MergedDictionaries.Add(resource); return Track(new Cleanup(() => Application.Current.Resources.MergedDictionaries.Remove(resource))); }
    public IDisposable RegisterLocalization(string defaultLanguage, IReadOnlyDictionary<string,IReadOnlyDictionary<string,string>> resources)
    { Localizer.Current.RegisterResources(PluginId,defaultLanguage,resources); return Track(new Cleanup(() => Localizer.Current.UnregisterResources(PluginId))); }
    public IDisposable Subscribe(Action<WorkbenchEvent> callback)
    {
        void Handler(WorkbenchEvent e) { try { if (!Lifetime.IsCancellationRequested) callback(e); } catch (Exception ex) { ReportError(ex); } }
        manager.Event += Handler; return Track(new Cleanup(() => manager.Event -= Handler));
    }
    public IDisposable Schedule(TimeSpan interval, Action callback)
    {
        var timer = new DispatcherTimer { Interval = interval };
        timer.Tick += (_, _) => { try { if (!Lifetime.IsCancellationRequested) callback(); } catch (Exception ex) { ReportError(ex); } };
        timer.Start(); return Track(new Cleanup(timer.Stop));
    }
    public void ReportError(Exception exception) => manager.Fail(plugin,exception);
    public void Cancel() => _lifetime.Cancel();
    public void Dispose()
    {
        Cancel();
        foreach (var resource in _resources.AsEnumerable().Reverse().ToArray())
            try { resource.Dispose(); } catch (Exception ex) { plugin.Error = ex.Message; }
        _resources.Clear(); _lifetime.Dispose();
    }
}
