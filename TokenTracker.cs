using System;
using System.IO;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Net;
using System.Net.Http;
using System.Collections.Generic;
using System.Web.Script.Serialization;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using Microsoft.Win32;

[assembly: System.Reflection.AssemblyTitle("Codex Token Tracker")]
[assembly: System.Reflection.AssemblyDescription("ChatGPT Work usage remaining in the Windows system tray")]
[assembly: System.Reflection.AssemblyVersion("1.2.0.0")]
[assembly: System.Reflection.AssemblyFileVersion("1.2.0.0")]
[assembly: System.Reflection.AssemblyInformationalVersion("1.2")]

static class Program {
    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
    [STAThread] static void Main(string[] args) {
        SetProcessDPIAware();
        ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        if (args.Length > 0 && args[0] == "--test") { Tests.Run(); return; }
        bool first;
        using (var mutex = new System.Threading.Mutex(true, "Local\\ChatGPTWorkTokenTracker", out first)) {
            if (first) Application.Run(new Tray());
        }
    }
}
class WindowUsage {
    public int Left;
    public long Reset;
}
class Usage {
    public string Plan;
    public WindowUsage Five, Week;
    public bool WeeklyOnly { get { return Plan.StartsWith("pro", StringComparison.OrdinalIgnoreCase); } }
    public int RefreshMilliseconds { get { return Plan.StartsWith("plus", StringComparison.OrdinalIgnoreCase) ? 20000 : 60000; } }
    public static Dictionary<string, object> Obj(object x) { return x as Dictionary<string, object>; }
    public static Usage Parse(string json) {
        var root = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json);
        var u = new Usage { Plan = Convert.ToString(root["plan_type"]) };
        var limits = Obj(root["rate_limit"]);
        if (limits == null) throw new Exception("Usage unavailable");
        foreach (string key in new[] { "primary_window", "secondary_window" }) {
            object raw;
            if (!limits.TryGetValue(key, out raw) || raw == null) continue;
            var w = Obj(raw);
            double used = Convert.ToDouble(w["used_percent"]);
            var value = new WindowUsage { Left = (int)Math.Floor(Math.Max(0, Math.Min(100, 100-used))), Reset = Convert.ToInt64(w["reset_at"]) };
            long seconds = Convert.ToInt64(w["limit_window_seconds"]);
            if (seconds == 18000) u.Five = value;
            if (seconds == 604800) u.Week = value;
        }
        if (u.Week == null && u.Five == null) throw new Exception("Usage unavailable");
        return u;
    }
    public static async Task<Usage> Read() {
        string home = Environment.GetEnvironmentVariable("CODEX_HOME");
        if (String.IsNullOrWhiteSpace(home)) home = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
        string path = Path.Combine(home, "auth.json");
        if (!File.Exists(path)) throw new Exception("Sign in to Codex to connect this account");
        var auth = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(path));
        object raw;
        if (!auth.TryGetValue("tokens", out raw) || Obj(raw) == null) throw new Exception("Sign in to Codex with your ChatGPT account");
        var tokens = Obj(raw);
        using (var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })) {
            client.Timeout = TimeSpan.FromSeconds(20);
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", Convert.ToString(tokens["access_token"]));
            client.DefaultRequestHeaders.Add("ChatGPT-Account-Id", Convert.ToString(tokens["account_id"]));
            using (var response = await client.GetAsync("https://chatgpt.com/backend-api/wham/usage")) {
                if (response.StatusCode == HttpStatusCode.Unauthorized) throw new Exception("Session expired - open Codex and sign in again");
                if (!response.IsSuccessStatusCode) throw new Exception("Usage unavailable (HTTP " + (int)response.StatusCode + ")");
                return Parse(await response.Content.ReadAsStringAsync());
            }
        }
    }
}
static class Artwork {
    [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr icon);
    public static Color Ink(int n, bool darkMode) { return n <= 10 ? Color.FromArgb(218,35,45) : n <= 25 ? Color.FromArgb(221,166,0) : darkMode ? Color.White : Color.Black; }
    public static bool IsDarkMode() {
        using (var key = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize")) {
            object value = key == null ? null : key.GetValue("SystemUsesLightTheme");
            if (value == null) value = key == null ? null : key.GetValue("AppsUseLightTheme");
            return value is int && (int)value == 0;
        }
    }
    public static Bitmap Draw(int size, int? top, int? bottom, bool two) {
        var bitmap = new Bitmap(size, size);
        Paint(bitmap, new Rectangle(0,0,size,two ? size/2 : size), top);
        if (two) Paint(bitmap, new Rectangle(0,size/2,size,size-size/2), bottom);
        return bitmap;
    }
    // Render Windows hinted text once at its final pixel size. Convert the
    // grayscale coverage into alpha; GDI text does not preserve bitmap alpha.
    static void Paint(Bitmap target, Rectangle box, int? number) {
        string text = number.HasValue ? number.Value.ToString() : "?";
        bool darkMode = IsDarkMode();
        Color ink = number.HasValue ? Ink(number.Value, darkMode) : darkMode ? Color.White : Color.Gray;
        var flags = TextFormatFlags.NoPadding | TextFormatFlags.NoClipping | TextFormatFlags.SingleLine;
        using (var mask = new Bitmap(128,128)) {
            for (float pixels = box.Height*1.5f; pixels >= 4f; pixels -= .5f) {
                using (var g = Graphics.FromImage(mask))
                using (var font = new Font("Segoe UI",pixels,FontStyle.Regular,GraphicsUnit.Pixel)) {
                    g.Clear(Color.Black);
                    TextRenderer.DrawText(g,text,font,new Point(4,4),Color.White,Color.Black,flags);
                }
                int left=128, top=128, right=-1, bottom=-1;
                for (int y=0;y<128;y++) for (int x=0;x<128;x++) {
                    var c=mask.GetPixel(x,y);
                    if (Math.Max(c.R,Math.Max(c.G,c.B)) < 12) continue;
                    left=Math.Min(left,x); right=Math.Max(right,x);
                    top=Math.Min(top,y); bottom=Math.Max(bottom,y);
                }
                int width=right-left+1, height=bottom-top+1;
                if (width<=0 || width>box.Width || height>box.Height-1) continue;
                int dx=box.X+(box.Width-width)/2, dy=box.Y+(box.Height-height)/2;
                for (int y=0;y<height;y++) for (int x=0;x<width;x++) {
                    var c=mask.GetPixel(left+x,top+y);
                    int alpha=(c.R*54+c.G*183+c.B*19)/256;
                    target.SetPixel(dx+x,dy+y,Color.FromArgb(alpha,ink));
                }
                return;
            }
        }
    }
    public static Icon Icon(int size, int? a, int? b, bool two) {
        using (var bitmap = Draw(size,a,b,two)) {
            IntPtr handle = bitmap.GetHicon();
            try { using (var borrowed = System.Drawing.Icon.FromHandle(handle)) return (Icon)borrowed.Clone(); }
            finally { DestroyIcon(handle); }
        }
    }
}
class Tray : ApplicationContext {
    readonly NotifyIcon tray = new NotifyIcon();
    readonly ContextMenuStrip menu = new ContextMenuStrip();
    readonly Timer timer = new Timer { Interval = 60000 };
    Usage usage;
    DateTime updated;
    string error = "Connecting...";
    bool busy, stopped;
    public Tray() {
        tray.ContextMenuStrip = menu;
        Update(); tray.Visible = true;
        menu.Opening += delegate { BuildMenu(); };
        timer.Tick += async delegate { await Refresh(); };
        timer.Start();
        var ignored = Refresh();
    }
    async Task Refresh() {
        if (busy || stopped) return;
        busy = true;
        try { var next = await Usage.Read(); if (stopped) return; usage = next; timer.Interval = next.RefreshMilliseconds; updated = DateTime.Now; error = null; }
        catch (Exception ex) { if (!stopped) { usage = null; error = ex is HttpRequestException || ex is TaskCanceledException ? "Offline - retrying" : ex is IOException ? "Cannot read account - retrying" : ex.Message; } }
        finally { busy = false; }
        if (!stopped) Update();
    }
    void Update() {
        bool two = usage != null && !usage.WeeklyOnly;
        int? a = usage == null ? (int?)null : two ? Left(usage.Five) : Left(usage.Week);
        Icon old = tray.Icon;
        tray.Icon = Artwork.Icon(Math.Max(16,SystemInformation.SmallIconSize.Width), a, usage == null ? null : Left(usage.Week), two);
        if (old != null) old.Dispose();
        string tip = usage == null ? "ChatGPT Work - usage unavailable" : two ? "ChatGPT Work: 5h " + Value(usage.Five) + " / week " + Value(usage.Week) : "ChatGPT Work: week " + Value(usage.Week);
        tray.Text = tip.Length > 63 ? tip.Substring(0,63) : tip;
        BuildMenu();
    }
    static int? Left(WindowUsage w) { return w == null ? (int?)null : w.Left; }
    static string Value(WindowUsage w) { return w == null ? "unavailable" : w.Left + "% left"; }
    void Info(string text) { menu.Items.Add(new ToolStripMenuItem(text) { Enabled = false }); }
    void BuildMenu() {
        while (menu.Items.Count > 0) { var item = menu.Items[0]; menu.Items.RemoveAt(0); item.Dispose(); }
        Info("ChatGPT Work · usage remaining");
        if (usage != null) {
            Info("Account: " + usage.Plan);
            if (!usage.WeeklyOnly) Details("Five-hour",usage.Five);
            Details("Weekly",usage.Week);
            Info("Updated " + updated.ToString("h:mm:ss tt"));
        } else Info(error ?? "Usage unavailable");
        menu.Items.Add(new ToolStripSeparator());
        var refresh = menu.Items.Add(busy ? "Refreshing..." : "Refresh now");
        refresh.Enabled = !busy;
        refresh.Click += async delegate { await Refresh(); };
        menu.Items.Add("Quit", null, delegate { ExitThread(); });
    }
    void Details(string label, WindowUsage w) {
        Info(label + ": " + Value(w));
        if (w != null) Info("Resets " + DateTimeOffset.FromUnixTimeSeconds(w.Reset).LocalDateTime.ToString("ddd, MMM d, h:mm tt"));
    }
    protected override void ExitThreadCore() {
        stopped = true; timer.Stop(); timer.Dispose(); tray.Visible = false;
        var icon = tray.Icon; tray.Dispose(); if (icon != null) icon.Dispose(); menu.Dispose(); base.ExitThreadCore();
    }
}
static class Tests {
    static void Check(bool ok) { if (!ok) throw new Exception("Test failed"); }
    public static void Run() {
        string w = "{\"used_percent\":25,\"limit_window_seconds\":604800,\"reset_at\":1790108424}";
        string f = "{\"used_percent\":95,\"limit_window_seconds\":18000,\"reset_at\":1790108424}";
        var pro = Usage.Parse("{\"plan_type\":\"prolite\",\"rate_limit\":{\"primary_window\":"+w+",\"secondary_window\":null}}");
        Check(pro.WeeklyOnly && pro.Week.Left == 75 && pro.Five == null && pro.RefreshMilliseconds == 60000);
        var plus = Usage.Parse("{\"plan_type\":\"plus\",\"rate_limit\":{\"primary_window\":"+w+",\"secondary_window\":"+f+"}}");
        Check(!plus.WeeklyOnly && plus.Five.Left == 5 && plus.Week.Left == 75 && plus.RefreshMilliseconds == 20000);
        Check(Artwork.Ink(10, false) != Artwork.Ink(11, false) && Artwork.Ink(25, false) != Artwork.Ink(26, false) && Artwork.Ink(100, false) == Color.Black && Artwork.Ink(100, true) == Color.White);
        Directory.CreateDirectory("checks");
        foreach (int size in new[] {16,20,24,32,48}) {
            using (var b = Artwork.Draw(size,100,null,false)) b.Save("checks/pro-"+size+".png");
            using (var b = Artwork.Draw(size,10,25,true)) b.Save("checks/plus-"+size+".png");
            using (var b = Artwork.Draw(size,99,null,false)) b.Save("checks/pro-99-"+size+".png");
        }
        var live = Usage.Read().GetAwaiter().GetResult();
        File.WriteAllText("checks/result.txt", "PASS: parsing, window order, thresholds, icon rendering. Live plan=" + live.Plan + "; weekly=" + (live.Week == null ? "unavailable" : live.Week.Left.ToString()) + "%");
    }
}
