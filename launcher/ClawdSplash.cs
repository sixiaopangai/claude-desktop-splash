// 小克开机动画启动器
// 用 Chrome（没装就用 Edge）的 kiosk 全屏模式播 splash.html，同时在后台拉起 Claude Desktop；
// 页面播完会改窗口标题（HANDOFF → DONE），这里轮询到后把播放窗口淡出并关掉。
//
// 用法：
//   ClawdSplash.exe            播动画 + 打开 Claude
//   ClawdSplash.exe --preview  只播动画，不打开 Claude
//
// 编译：见同目录 build.ps1（只用系统自带的 .NET Framework csc.exe，不下载任何东西）

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

static class ClawdSplash
{
    const string ClaudeAppId = @"shell:AppsFolder\Claude_pzs8sxrjxfjjc!Claude";
    const string TitleMark = "小克开机动画";
    const int TimeoutMs = 40000;                      // 动画约 21 秒，加上 Chrome 冷启动留足余量

    [STAThread]
    static void Main(string[] args)
    {
        bool preview = args.Contains("--preview");
        string dir = AppDomain.CurrentDomain.BaseDirectory;
        string html = new[] { Path.Combine(dir, "splash.html"), Path.Combine(dir, "..", "splash.html") }
            .Select(Path.GetFullPath).FirstOrDefault(File.Exists);
        // 优先用 Chrome，没装再退回 Edge
        string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string browser = new[] {
            Path.Combine(pf, @"Google\Chrome\Application\chrome.exe"),
            Path.Combine(pf86, @"Google\Chrome\Application\chrome.exe"),
            Path.Combine(local, @"Google\Chrome\Application\chrome.exe"),
            Path.Combine(pf86, @"Microsoft\Edge\Application\msedge.exe"),
            Path.Combine(pf, @"Microsoft\Edge\Application\msedge.exe"),
        }.FirstOrDefault(File.Exists);

        // 缺东西就别折腾了，直接开 Claude
        if (html == null || browser == null)
        {
            Log(dir, "missing " + (html == null ? "splash.html" : "chrome.exe / msedge.exe") + ", launching Claude directly");
            if (!preview) LaunchClaude();
            return;
        }

        bool isEdge = browser.EndsWith("msedge.exe", StringComparison.OrdinalIgnoreCase);
        string profile = Path.Combine(dir, isEdge ? "edge-profile" : "chrome-profile");
        string url = new Uri(html).AbsoluteUri + "?embed";
        int mcp = CountMcpServers();
        if (mcp >= 0) url += "&mcp=" + mcp;                  // 开场终端里那行 MCP 读数用的是真数
        // 独立的配置目录 = 独立的浏览器进程，关它不会碰到你平时开着的浏览器；
        // 后面这些开关把后台联网、组件更新、同步之类全关掉，只留播动画要用的
        var edgeProc = Process.Start(new ProcessStartInfo(browser,
            "--kiosk \"" + url + "\"" + (isEdge ? " --edge-kiosk-type=fullscreen" : "") +
            " --user-data-dir=\"" + profile + "\"" +
            " --no-first-run --no-default-browser-check --hide-crash-restore-bubble" +
            " --disable-background-networking --disable-component-update --disable-default-apps" +
            " --disable-sync --disable-extensions --no-pings" +
            " --disable-features=Translate,msEdgeSidebarV2")
        { UseShellExecute = false });

        IntPtr hwnd = IntPtr.Zero;
        bool claudeStarted = false;
        var clock = Stopwatch.StartNew();
        long foundAt = -1, lostAt = -1;

        while (clock.ElapsedMilliseconds < TimeoutMs)
        {
            // Edge 进 kiosk 全屏时会销毁最早的窗口、换一个新的，句柄失效就重新找
            if (hwnd != IntPtr.Zero && !IsWindow(hwnd))
            {
                hwnd = IntPtr.Zero;
                lostAt = clock.ElapsedMilliseconds;
            }
            if (hwnd == IntPtr.Zero)
            {
                hwnd = FindWindowByTitle(TitleMark);
                if (hwnd != IntPtr.Zero)
                {
                    foundAt = clock.ElapsedMilliseconds;
                    SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);
                    SetForegroundWindow(hwnd);
                }
            }

            // 播放窗口置顶以后再拉起 Claude，让它在动画背后慢慢加载
            if (!preview && !claudeStarted &&
                ((foundAt >= 0 && clock.ElapsedMilliseconds - foundAt > 1200) || clock.ElapsedMilliseconds > 6000))
            {
                LaunchClaude();
                claudeStarted = true;
            }

            if (hwnd != IntPtr.Zero)
            {
                string title = GetTitle(hwnd);
                if (title.Contains("HANDOFF") || title.Contains("DONE"))
                {
                    FadeOut(hwnd, 650);
                    break;
                }
            }
            else if (edgeProc.HasExited || (lostAt >= 0 && clock.ElapsedMilliseconds - lostAt > 2500))
            {
                break;                                   // Edge 没起来，或播放窗口被手动关掉了
            }
            Thread.Sleep(40);
        }

        if (hwnd != IntPtr.Zero && IsWindow(hwnd)) PostMessage(hwnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
        try { if (!edgeProc.WaitForExit(3000)) edgeProc.Kill(); } catch { }

        if (!preview)
        {
            if (!claudeStarted) LaunchClaude();
            else { Thread.Sleep(150); LaunchClaude(); }   // 再调一次 = 把已打开的 Claude 窗口叫到前台
        }
    }

    static void LaunchClaude()
    {
        try { Process.Start(new ProcessStartInfo("explorer.exe", ClaudeAppId) { UseShellExecute = true }); }
        catch (Exception e) { Log(AppDomain.CurrentDomain.BaseDirectory, "launch failed: " + e.Message); }
    }

    // 读 Claude Desktop 的配置数 MCP 服务器；读不到返回 -1，页面就改显示「TOOLS READY」
    static int CountMcpServers()
    {
        string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        foreach (string f in new[] {
            Path.Combine(roaming, @"Claude\claude_desktop_config.json"),
            Path.Combine(local, @"Packages\Claude_pzs8sxrjxfjjc\LocalCache\Roaming\Claude\claude_desktop_config.json") })
        {
            try
            {
                if (!File.Exists(f)) continue;
                var cfg = new System.Web.Script.Serialization.JavaScriptSerializer()
                    .DeserializeObject(File.ReadAllText(f, Encoding.UTF8)) as System.Collections.Generic.Dictionary<string, object>;
                object servers;
                if (cfg != null && cfg.TryGetValue("mcpServers", out servers))
                {
                    var dict = servers as System.Collections.Generic.Dictionary<string, object>;
                    return dict == null ? 0 : dict.Count;
                }
                return 0;
            }
            catch { }
        }
        return -1;
    }

    // 把播放窗口整体淡出，和页面里的「后撤 + 失焦」叠在一起，露出后面的 Claude
    static void FadeOut(IntPtr hwnd, int ms)
    {
        int ex = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, ex | WS_EX_LAYERED);
        const int steps = 26;
        for (int i = steps; i >= 0; i--)
        {
            SetLayeredWindowAttributes(hwnd, 0, (byte)(255 * i / steps), LWA_ALPHA);
            Thread.Sleep(ms / steps);
        }
    }

    static IntPtr FindWindowByTitle(string mark)
    {
        IntPtr found = IntPtr.Zero;
        EnumWindows((h, _) =>
        {
            if (IsWindowVisible(h) && GetTitle(h).Contains(mark)) { found = h; return false; }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    static string GetTitle(IntPtr h)
    {
        var sb = new StringBuilder(512);
        GetWindowText(h, sb, sb.Capacity);
        return sb.ToString();
    }

    static void Log(string dir, string msg)
    {
        try { File.AppendAllText(Path.Combine(dir, "launcher.log"), DateTime.Now.ToString("s") + " " + msg + Environment.NewLine); }
        catch { }
    }

    // ── Win32 ────────────────────────────────────────────────────────────
    delegate bool EnumProc(IntPtr hWnd, IntPtr lParam);
    static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
    const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_SHOWWINDOW = 0x40;
    const int GWL_EXSTYLE = -20, WS_EX_LAYERED = 0x80000;
    const uint LWA_ALPHA = 0x2, WM_CLOSE = 0x10;

    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb, IntPtr lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder s, int max);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] static extern bool IsWindow(IntPtr h);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int idx);
    [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr h, int idx, int val);
    [DllImport("user32.dll")] static extern bool SetLayeredWindowAttributes(IntPtr h, uint key, byte alpha, uint flags);
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
}
