using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace NexusM.Services;

/// <summary>
/// Windows system tray icon using pure Win32 Shell_NotifyIcon API.
/// Runs on a dedicated background thread with its own message loop.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class TrayIconService : IDisposable
{
    private readonly int _port;
    private readonly string _logFilePath;
    private readonly string _configFilePath;
    private readonly ConfigService _configService;
    private readonly IHostApplicationLifetime _lifetime;
    private Thread? _thread;
    private bool _disposed;
    private IntPtr _hwnd;

    // Menu item IDs
    private const int ID_OPEN_UI = 1001;
    private const int ID_SHOW_LOG = 1002;
    private const int ID_OPEN_CONFIG = 1003;
    private const int ID_RUN_ON_STARTUP = 1005;
    private const int ID_EXIT = 1004;

    // Windows messages
    private const int WM_APP_TRAYICON = 0x8000; // WM_APP
    private const int WM_LBUTTONDBLCLK = 0x0203;
    private const int WM_RBUTTONUP = 0x0205;
    private const int WM_COMMAND = 0x0111;
    private const int WM_DESTROY = 0x0002;

    // NotifyIcon flags
    private const int NIF_MESSAGE = 0x01;
    private const int NIF_ICON = 0x02;
    private const int NIF_TIP = 0x04;
    private const int NIM_ADD = 0x00;
    private const int NIM_DELETE = 0x02;

    // Menu flags
    private const int MF_STRING = 0x0000;
    private const int MF_SEPARATOR = 0x0800;
    private const int MF_CHECKED = 0x0008;
    private const int MF_UNCHECKED = 0x0000;
    private const int TPM_RIGHTBUTTON = 0x0002;
    private const int TPM_RETURNCMD = 0x0100;

    public TrayIconService(int port, string logFilePath, string configFilePath, ConfigService configService, IHostApplicationLifetime lifetime)
    {
        _port = port;
        _logFilePath = logFilePath;
        _configFilePath = configFilePath;
        _configService = configService;
        _lifetime = lifetime;

        _thread = new Thread(RunMessageLoop) { IsBackground = true, Name = "NexusM-Tray" };
        _thread.Start();
    }

    private void RunMessageLoop()
    {
        // Register a minimal window class for receiving tray messages
        var className = "NexusM_TrayWnd_" + Process.GetCurrentProcess().Id;

        _wndProc = WndProc; // prevent GC of delegate

        var wc = new WNDCLASS
        {
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
            lpszClassName = className
        };

        var atom = RegisterClass(ref wc);
        if (atom == 0) return;

        _hwnd = CreateWindowEx(0, className, "NexusM Tray", 0, 0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        if (_hwnd == IntPtr.Zero) return;

        // Load icon from the running exe
        var exePath = Process.GetCurrentProcess().MainModule?.FileName ?? "";
        var hIcon = ExtractIcon(IntPtr.Zero, exePath, 0);
        if (hIcon == IntPtr.Zero)
            hIcon = LoadIcon(IntPtr.Zero, new IntPtr(32512)); // IDI_APPLICATION

        // Add tray icon
        var nid = new NOTIFYICONDATA
        {
            cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = _hwnd,
            uID = 1,
            uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
            uCallbackMessage = WM_APP_TRAYICON,
            hIcon = hIcon,
            szTip = $"NexusM \u2014 Port {_port}"
        };
        Shell_NotifyIcon(NIM_ADD, ref nid);

        // Message loop
        while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }

        // Cleanup
        Shell_NotifyIcon(NIM_DELETE, ref nid);
        DestroyWindow(_hwnd);
    }

    private WndProcDelegate? _wndProc;

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_APP_TRAYICON)
        {
            int trayMsg = lParam.ToInt32() & 0xFFFF;
            if (trayMsg == WM_RBUTTONUP)
                ShowContextMenu();
            else if (trayMsg == WM_LBUTTONDBLCLK)
                OpenBrowser();
        }
        else if (msg == WM_COMMAND)
        {
            int id = wParam.ToInt32() & 0xFFFF;
            switch (id)
            {
                case ID_OPEN_UI: OpenBrowser(); break;
                case ID_SHOW_LOG: OpenFolder(_logFilePath); break;
                case ID_OPEN_CONFIG: OpenFile(_configFilePath); break;
                case ID_RUN_ON_STARTUP: ToggleRunOnStartup(); break;
                case ID_EXIT: _lifetime.StopApplication(); break;
            }
        }

        return DefWindowProc(hwnd, msg, wParam, lParam);
    }

    private void ShowContextMenu()
    {
        var startupChecked = _configService.Config.Server.RunOnStartup ? MF_CHECKED : MF_UNCHECKED;
        var menu = CreatePopupMenu();
        AppendMenu(menu, MF_STRING, ID_OPEN_UI, "Open NexusM");
        AppendMenu(menu, MF_SEPARATOR, 0, null);
        AppendMenu(menu, MF_STRING, ID_SHOW_LOG, "Open Logs Folder");
        AppendMenu(menu, MF_STRING, ID_OPEN_CONFIG, "Open Config");
        AppendMenu(menu, MF_STRING | startupChecked, ID_RUN_ON_STARTUP, "Run on Startup");
        AppendMenu(menu, MF_SEPARATOR, 0, null);
        AppendMenu(menu, MF_STRING, ID_EXIT, "Exit");

        GetCursorPos(out var pt);
        SetForegroundWindow(_hwnd);
        int cmd = TrackPopupMenu(menu, TPM_RIGHTBUTTON | TPM_RETURNCMD, pt.X, pt.Y, 0, _hwnd, IntPtr.Zero);
        DestroyMenu(menu);

        if (cmd > 0)
            PostMessage(_hwnd, WM_COMMAND, new IntPtr(cmd), IntPtr.Zero);
    }

    private void OpenBrowser()
    {
        try { Process.Start(new ProcessStartInfo($"http://localhost:{_port}") { UseShellExecute = true }); }
        catch { /* ignore */ }
    }

    private static void OpenFolder(string filePath)
    {
        try
        {
            var dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                Process.Start(new ProcessStartInfo(dir) { UseShellExecute = true });
        }
        catch { /* ignore */ }
    }

    private void ToggleRunOnStartup()
    {
        var newValue = !_configService.Config.Server.RunOnStartup;
        _configService.Config.Server.RunOnStartup = newValue;
        StartupRegistryHelper.SetRunOnStartup(newValue);
        _configService.SaveConfig();
    }

    private static void OpenFile(string path)
    {
        try
        {
            if (File.Exists(path))
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch { /* ignore */ }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_hwnd != IntPtr.Zero)
            PostMessage(_hwnd, 0x0012 /* WM_QUIT */, IntPtr.Zero, IntPtr.Zero);
    }

    // ── P/Invoke declarations ────────────────────────────────────────

    private delegate IntPtr WndProcDelegate(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASS
    {
        public int style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public int uID;
        public int uFlags;
        public int uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public int message;
        public IntPtr wParam;
        public IntPtr lParam;
        public int time;
        public POINT pt;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern ushort RegisterClass(ref WNDCLASS wc);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowEx(int exStyle, string className, string windowName, int style,
        int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern int GetMessage(out MSG msg, IntPtr hwnd, int filterMin, int filterMax);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref MSG msg);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref MSG msg);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT point);

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool AppendMenu(IntPtr menu, int flags, int id, string? text);

    [DllImport("user32.dll")]
    private static extern int TrackPopupMenu(IntPtr menu, int flags, int x, int y, int reserved, IntPtr hwnd, IntPtr rect);

    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(IntPtr menu);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIcon(int message, ref NOTIFYICONDATA data);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr ExtractIcon(IntPtr hInst, string lpszExeFileName, int nIconIndex);

    [DllImport("user32.dll")]
    private static extern IntPtr LoadIcon(IntPtr hInstance, IntPtr lpIconName);
}
