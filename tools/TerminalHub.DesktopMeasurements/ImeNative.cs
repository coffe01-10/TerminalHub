using System.Runtime.InteropServices;
using System.Text;
using SkiaSharp;

namespace TerminalHub.DesktopMeasurements;

/// <summary>Raised when the OS foreground window is not the expected test HWND,
/// so a real-key batch must be skipped rather than typed into someone else's
/// window. Callers mark the case aborted.</summary>
internal sealed class ForegroundAbort() : Exception("foreground window is not the test window; real keys skipped");

/// <summary>Win32 primitives for --ime-acceptance: foreground-guarded atomic
/// SendInput batches, thread-scoped keyboard-layout activation, IMM open state,
/// candidate-window discovery and raw screen-pixel capture. Keyboard layout
/// changes are scoped to this process' UI thread and restored by the caller —
/// the user's global input-method defaults are never touched.</summary>
internal static class ImeNative
{
    public const uint InputKeyboard = 1;
    public const uint KeyUp = 0x0002;
    public const uint KeyUnicode = 0x0004;
    public const uint KlfActivate = 0x00000001;
    public const uint WmInputLangChangeRequest = 0x0050;
    public const int SrcCopy = 0x00CC0020;
    public const int CaptureBlt = 0x40000000;

    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    public struct Rect
    {
        public int Left, Top, Right, Bottom;
        public int Width => Right - Left;
        public int Height => Bottom - Top;
        public override string ToString() => $"{Left},{Top},{Width}x{Height}";
    }

    // INPUT's union is 32 bytes on every supported arch; keyboard data starts at offset 8.
    [StructLayout(LayoutKind.Explicit, Size = 40)]
    public struct Input { [FieldOffset(0)] public uint Type; [FieldOffset(8)] public KeybdInput Ki; }
    [StructLayout(LayoutKind.Sequential)]
    public struct KeybdInput { public ushort Vk, Scan; public uint Flags, Time; public IntPtr ExtraInfo; }
    [StructLayout(LayoutKind.Sequential)]
    public struct BitmapInfoHeader { public int Size, Width, Height; public short Planes, BitCount; public int Compression, SizeImage, XPpm, YPpm, ClrUsed, ClrImportant; }

    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint attach, uint attachTo, bool fAttach);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int cmd);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out Rect rect);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr LoadKeyboardLayout(string klid, uint flags);
    [DllImport("user32.dll")] public static extern bool UnloadKeyboardLayout(IntPtr hkl);
    [DllImport("user32.dll")] public static extern IntPtr ActivateKeyboardLayout(IntPtr hkl, uint flags);
    [DllImport("user32.dll")] public static extern IntPtr GetKeyboardLayout(uint threadId);
    [DllImport("user32.dll")] public static extern int GetKeyboardLayoutList(int size, [Out] IntPtr[] list);
    [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] public static extern uint SendInput(uint count, Input[] inputs, int size);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc proc, IntPtr param);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr hWnd, StringBuilder name, int max);
    [DllImport("user32.dll")] public static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr hWnd, IntPtr dc);
    [DllImport("imm32.dll")] public static extern IntPtr ImmGetContext(IntPtr hWnd);
    [DllImport("imm32.dll")] public static extern bool ImmReleaseContext(IntPtr hWnd, IntPtr ctx);
    [DllImport("imm32.dll")] public static extern bool ImmSetOpenStatus(IntPtr ctx, bool open);
    [DllImport("imm32.dll")] public static extern bool ImmGetOpenStatus(IntPtr ctx);
    [DllImport("imm32.dll", CharSet = CharSet.Unicode)] public static extern uint ImmGetDescription(IntPtr hkl, StringBuilder desc, uint size);
    [DllImport("dwmapi.dll")] public static extern int DwmFlush();
    [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] public static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] public static extern IntPtr CreateDIBSection(IntPtr dc, ref BitmapInfoHeader bmi, uint usage, out IntPtr bits, IntPtr section, uint offset);
    [DllImport("gdi32.dll")] public static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] public static extern bool BitBlt(IntPtr dc, int x, int y, int w, int h, IntPtr src, int sx, int sy, int rop);
    [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr obj);

    /// <summary>Bring our own test window foreground; attach-thread fallback for
    /// the case where the OS refuses a plain SetForegroundWindow.</summary>
    public static bool EnsureForeground(IntPtr hwnd)
    {
        if (GetForegroundWindow() == hwnd) return true;
        SetForegroundWindow(hwnd);
        if (GetForegroundWindow() == hwnd) return true;
        var fg = GetForegroundWindow();
        var fgThread = GetWindowThreadProcessId(fg, out _);
        var self = GetCurrentThreadId();
        if (fg != IntPtr.Zero && fgThread != self && AttachThreadInput(self, fgThread, true))
        {
            try { SetForegroundWindow(hwnd); }
            finally { AttachThreadInput(self, fgThread, false); }
        }
        return GetForegroundWindow() == hwnd;
    }

    private static Input Key(ushort vk, ushort scan, uint flags)
        => new() { Type = InputKeyboard, Ki = new KeybdInput { Vk = vk, Scan = scan, Flags = flags } };

    /// <summary>Inject only while the expected test HWND owns the foreground —
    /// anything else aborts the scenario; keys never go to a foreign window.
    /// No reclaim here: foreground is only taken when this run created and
    /// activated its own window, never wrested back mid-scenario.</summary>
    private static void SendBatch(IntPtr owner, Input[] inputs)
    {
        if (GetForegroundWindow() != owner) throw new ForegroundAbort();
        if (SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>()) != inputs.Length)
            throw new ForegroundAbort();
    }

    /// <summary>Real key press as one atomic down+up batch; aborts before typing
    /// if our window is not foreground. <paramref name="owner"/> is the HWND the
    /// keys are meant for — never send to a foreign foreground window.</summary>
    public static void Tap(IntPtr owner, ushort vk) => SendBatch(owner, [Key(vk, 0, 0), Key(vk, 0, KeyUp)]);
    public static void UnicodeChar(IntPtr owner, char c) => SendBatch(owner, [Key(0, c, KeyUnicode), Key(0, c, KeyUnicode | KeyUp)]);
    public static void TypeText(IntPtr owner, string text) { foreach (var c in text) UnicodeChar(owner, c); }
    /// <summary>Ctrl+C as a single 4-INPUT batch so a partial failure cannot
    /// leave the modifier latched.</summary>
    public static void CtrlC(IntPtr owner) => SendBatch(owner,
        [Key(0xA2, 0, 0), Key((ushort)'C', 0, 0), Key((ushort)'C', 0, KeyUp), Key(0xA2, 0, KeyUp)]);
    /// <summary>Win+Space cycles the focused thread's input profile — the
    /// supported way to select a TSF IME (e.g. Microsoft Pinyin) when the
    /// keyboard-layout list only holds a plain zh-CN layout. Per-thread,
    /// not a machine-global change.</summary>
    public static void CycleInputLanguage(IntPtr owner) => SendBatch(owner,
        [Key(0x5B, 0, 0), Key(0x20, 0, 0), Key(0x20, 0, KeyUp), Key(0x5B, 0, KeyUp)]);

    /// <summary>Activate a zh-CN input method on this thread only, preferring a
    /// loaded HKL whose IMM description names Microsoft Pinyin; otherwise the
    /// 0804 layout is loaded (reported via <paramref name="loaded"/>) and the
    /// focused window is asked to switch. Returns the now-active HKL.</summary>
    public static IntPtr ActivateChineseIme(IntPtr hwnd, out bool loaded)
    {
        loaded = false;
        var count = Math.Max(GetKeyboardLayoutList(0, null!), 0);
        var hkls = new IntPtr[Math.Max(count, 1)];
        GetKeyboardLayoutList(hkls.Length, hkls);
        var sb = new StringBuilder(64);
        IntPtr zh = IntPtr.Zero, target = IntPtr.Zero;
        foreach (var hkl in hkls)
        {
            if (((long)hkl & 0xFFFF) != 0x0804) continue;
            if (zh == IntPtr.Zero) zh = hkl;
            sb.Clear();
            if (target == IntPtr.Zero && ImmGetDescription(hkl, sb, (uint)sb.Capacity) > 0
                && (sb.ToString().Contains("Pinyin", StringComparison.OrdinalIgnoreCase) || sb.ToString().Contains("拼音")))
                target = hkl;
        }
        if (target == IntPtr.Zero) target = zh;
        if (target == IntPtr.Zero)
        {
            target = LoadKeyboardLayout("00000804", KlfActivate);
            loaded = target != IntPtr.Zero;
        }
        if (target == IntPtr.Zero) return IntPtr.Zero;
        ActivateKeyboardLayout(target, 0);
        SendMessage(hwnd, WmInputLangChangeRequest, IntPtr.Zero, target);
        return GetKeyboardLayout(0);
    }

    // Text Services Framework: per-process TIP profile activation. A TSF IME
    // (e.g. Microsoft Pinyin) does NOT engage through ActivateKeyboardLayout —
    // that only swaps the keyboard layout and letters reach the app literally.
    // ActivateProfile with TF_IPPMF_FORPROCESS turns the TIP on for this
    // process' input threads without touching the user's global profile.
    [ComImport, Guid("71C6E74C-0F28-11D8-A82A-00065B84435C"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITfInputProcessorProfileMgr
    {
        [PreserveSig]
        int ActivateProfile(uint profileType, ushort langid, [In] in Guid clsid,
            [In] in Guid profile, IntPtr hkl, uint flags);
    }
    [DllImport("ole32.dll")] private static extern int CoCreateInstance([In] in Guid clsid,
        [MarshalAs(UnmanagedType.IUnknown)] object? outer, uint clsctx, [In] in Guid iid,
        [MarshalAs(UnmanagedType.Interface)] out object obj);
    private static readonly Guid IID_ITfInputProcessorProfileMgr = typeof(ITfInputProcessorProfileMgr).GUID;
    private static readonly Guid CLSID_TF_InputProcessorProfiles = new("33C53A50-F456-4884-B049-85FD643ECFED");
    private const uint TfProfileTypeInputProcessor = 1;
    private const uint TfIppmfForProcess = 0x10000000;
    // Microsoft Pinyin TIP clsid + profile guid (the zh-CN IME installed with
    // the zh-CN language pack); constants of the OS, not secrets.
    private static readonly Guid MS_Pinyin_Clsid = new("81D4E9C9-1D3B-41BC-9E6C-4B40BF79E35E");
    private static readonly Guid MS_Pinyin_Profile = new("FA550B04-5AD7-411F-A5AC-CA038EC515D7");

    /// <summary>Activate Microsoft Pinyin for this process' input threads
    /// (TF_IPPMF_FORPROCESS); the user's global profile is untouched. Returns
    /// the HRESULT of the last failed step or 0 — callers record it.</summary>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public static int ActivateMsPinyinForProcess()
    {
        try
        {
            var hr = CoCreateInstance(in CLSID_TF_InputProcessorProfiles, null, 0x17,
                in IID_ITfInputProcessorProfileMgr, out var obj);
            if (hr != 0) return hr;
            var mgr = (ITfInputProcessorProfileMgr)obj;
            try
            {
                return mgr.ActivateProfile(TfProfileTypeInputProcessor, 0x0804,
                    in MS_Pinyin_Clsid, in MS_Pinyin_Profile, IntPtr.Zero, TfIppmfForProcess);
            }
            finally { Marshal.ReleaseComObject(obj); }
        }
        catch (Exception ex) { return ex.HResult; }
    }

    /// <summary>Read-only: the IMM description for an HKL, when the OS exposes
    /// one — recorded as evidence, never used to claim a specific profile.</summary>
    public static string? ImeDescription(IntPtr hkl)
    {
        var sb = new StringBuilder(64);
        return ImmGetDescription(hkl, sb, (uint)sb.Capacity) > 0 ? sb.ToString() : null;
    }

    public static bool? GetImeOpen(IntPtr hwnd)
    {
        var imc = ImmGetContext(hwnd);
        if (imc == IntPtr.Zero) return null;
        try { return ImmGetOpenStatus(imc); } finally { ImmReleaseContext(hwnd, imc); }
    }

    public static bool? SetImeOpen(IntPtr hwnd, bool open)
    {
        var imc = ImmGetContext(hwnd);
        if (imc == IntPtr.Zero) return null;
        try { return ImmSetOpenStatus(imc, open); } finally { ImmReleaseContext(hwnd, imc); }
    }

    /// <summary>Visible windows whose class names a dedicated IME/candidate
    /// surface. Heuristic evidence only — class + rect, never window titles;
    /// generic CoreWindow/XAML popup hosts are intentionally excluded.</summary>
    public static List<(string Class, Rect Bounds)> CandidateWindows()
    {
        var found = new List<(string, Rect)>();
        var sb = new StringBuilder(256);
        EnumWindows((h, _) =>
        {
            if (!IsWindowVisible(h)) return true;
            sb.Clear();
            GetClassName(h, sb, sb.Capacity);
            var cls = sb.ToString();
            if (!cls.Contains("IME", StringComparison.OrdinalIgnoreCase)
                && !cls.Contains("Candidate", StringComparison.OrdinalIgnoreCase)
                && !cls.Contains("Cicero", StringComparison.OrdinalIgnoreCase)
                && !cls.Contains("TextInput", StringComparison.OrdinalIgnoreCase)) return true;
            GetWindowRect(h, out var r);
            if (r.Width > 0 && r.Height > 0) found.Add((cls, r));
            return true;
        }, IntPtr.Zero);
        return found;
    }

    // Display-adapter evidence for whether a hardware GPU existed (deciding
    // if "default rendering" actually engaged a GPU path). EnumDisplayDevices
    // is COM-free and reliable even on indirect-display VMs where DXGI itself
    // may not be queryable.
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DisplayDevice
    {
        public int Cb;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
        public int StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceId;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
    }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplayDevices(string? device, uint devNum, ref DisplayDevice dd, uint flags);

    /// <summary>Active display adapters as "DeviceString (DeviceId)". On a VM
    /// with only a virtual/indirect display driver the absence of any real
    /// GPU name is itself the finding — callers flag non-hardware entries.
    /// </summary>
    public static List<string> DisplayAdapters()
    {
        var result = new List<string>();
        var dd = new DisplayDevice { Cb = Marshal.SizeOf<DisplayDevice>() };
        for (uint i = 0; EnumDisplayDevices(null, i, ref dd, 0); i++)
        {
            if ((dd.StateFlags & 1) == 0) continue; // DISPLAY_DEVICE_ACTIVE
            var s = dd.DeviceString?.TrimEnd('\0') ?? "";
            var id = dd.DeviceId?.TrimEnd('\0') ?? "";
            var softwareish = s.Contains("Basic Render", StringComparison.OrdinalIgnoreCase)
                || s.Contains("IddSample", StringComparison.OrdinalIgnoreCase)
                || s.Contains("Indirect", StringComparison.OrdinalIgnoreCase)
                || s.Contains("Remote", StringComparison.OrdinalIgnoreCase)
                || s.Contains("Virtual", StringComparison.OrdinalIgnoreCase);
            result.Add($"{s} [{id}]{(softwareish ? " [non-hardware]" : " [hardware]")}");
        }
        return result;
    }

    /// <summary>Real screen pixels of exactly <paramref name="area"/> (BitBlt +
    /// CAPTUREBLT so layered popups show up), clipped to the virtual screen and
    /// encoded as PNG. All GDI objects are released even on failure.</summary>
    public static void Capture(Rect area, string path)
    {
        int vx = GetSystemMetrics(76), vy = GetSystemMetrics(77), vw = GetSystemMetrics(78), vh = GetSystemMetrics(79);
        var r = new Rect
        {
            Left = Math.Max(vx, area.Left), Top = Math.Max(vy, area.Top),
            Right = Math.Min(vx + vw, area.Right), Bottom = Math.Min(vy + vh, area.Bottom)
        };
        if (r.Width <= 0 || r.Height <= 0) throw new InvalidOperationException("capture rect outside the virtual screen");
        var screen = GetDC(IntPtr.Zero);
        var mem = IntPtr.Zero; var dib = IntPtr.Zero;
        try
        {
            var bmi = new BitmapInfoHeader { Size = 40, Width = r.Width, Height = -r.Height, Planes = 1, BitCount = 32 };
            dib = CreateDIBSection(screen, ref bmi, 0, out var bits, IntPtr.Zero, 0);
            mem = CreateCompatibleDC(screen);
            var old = SelectObject(mem, dib);
            BitBlt(mem, 0, 0, r.Width, r.Height, screen, r.Left, r.Top, SrcCopy | CaptureBlt);
            SelectObject(mem, old);
            var bytes = new byte[r.Width * r.Height * 4];
            Marshal.Copy(bits, bytes, 0, bytes.Length);
            using var bmp = new SKBitmap(r.Width, r.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
            Marshal.Copy(bytes, 0, bmp.GetPixels(), bytes.Length);
            using var image = SKImage.FromBitmap(bmp);
            using var data = image.Encode(SKEncodedImageFormat.Png, 90);
            File.WriteAllBytes(path, data.ToArray());
        }
        finally
        {
            if (mem != IntPtr.Zero) DeleteDC(mem);
            if (dib != IntPtr.Zero) DeleteObject(dib);
            ReleaseDC(IntPtr.Zero, screen);
        }
    }
}
