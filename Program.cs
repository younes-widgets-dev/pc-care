using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace PCCare
{
    // ================= Crash log =================
    static class Crash
    {
        public static void Install()
        {
            try
            {
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                Application.ThreadException += delegate (object s, ThreadExceptionEventArgs e) { Log.Add("ERROR " + e.Exception); };
                AppDomain.CurrentDomain.UnhandledException += delegate (object s, UnhandledExceptionEventArgs e) { Log.Add("ERROR " + e.ExceptionObject); };
            }
            catch { }
        }
    }

    // ================= Settings =================
    static class Cfg
    {
        public static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PCCare");
        static readonly string FileP = Path.Combine(Dir, "settings.ini");
        static Dictionary<string, string> d = new Dictionary<string, string>();
        static object lk = new object();
        public static void Load()
        {
            try
            {
                Directory.CreateDirectory(Dir);
                if (!File.Exists(FileP)) return;
                foreach (string l in File.ReadAllLines(FileP, Encoding.UTF8)) { int i = l.IndexOf('='); if (i > 0) d[l.Substring(0, i)] = l.Substring(i + 1); }
            }
            catch { }
        }
        public static void Save()
        {
            try
            {
                lock (lk)
                {
                    Directory.CreateDirectory(Dir); List<string> o = new List<string>();
                    foreach (KeyValuePair<string, string> kv in d) o.Add(kv.Key + "=" + kv.Value);
                    File.WriteAllLines(FileP, o.ToArray(), Encoding.UTF8);
                }
            }
            catch { }
        }
        public static string S(string k, string def) { lock (lk) { string v; return d.TryGetValue(k, out v) ? v : def; } }
        public static int I(string k, int def) { int v; return int.TryParse(S(k, ""), out v) ? v : def; }
        public static long L(string k, long def) { long v; return long.TryParse(S(k, ""), out v) ? v : def; }
        public static bool B(string k, bool def) { string v = S(k, ""); return v == "" ? def : v == "1"; }
        public static void Set(string k, string v) { lock (lk) { d[k] = v; } }
        public static void Set(string k, bool v) { Set(k, v ? "1" : "0"); }
    }

    // ================= Log =================
    static class Log
    {
        static readonly string FileP = Path.Combine(Cfg.Dir, "log.txt");
        static object lk = new object();
        public static string PathOf { get { return FileP; } }
        public static void Add(string s)
        {
            try
            {
                lock (lk)
                {
                    Directory.CreateDirectory(Cfg.Dir);
                    try { if (File.Exists(FileP) && new FileInfo(FileP).Length > 300000) { string[] all = File.ReadAllLines(FileP); int n = all.Length / 2; string[] keep = new string[all.Length - n]; Array.Copy(all, n, keep, 0, keep.Length); File.WriteAllLines(FileP, keep); } } catch { }
                    File.AppendAllText(FileP, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + s + "\r\n");
                }
            }
            catch { }
        }
        public static string Tail(int lines)
        {
            try
            {
                lock (lk)
                {
                    if (!File.Exists(FileP)) return "(log is empty)";
                    string[] all = File.ReadAllLines(FileP); int from = Math.Max(0, all.Length - lines);
                    StringBuilder sb = new StringBuilder(); for (int i = all.Length - 1; i >= from; i--) sb.AppendLine(all[i]); return sb.ToString();
                }
            }
            catch { return "(cannot read log)"; }
        }
    }

    // ================= Native =================
    static class N
    {
        [StructLayout(LayoutKind.Sequential)] struct LASTINPUTINFO { public uint cbSize; public uint dwTime; }
        [StructLayout(LayoutKind.Sequential)] struct MEMSTAT { public uint dwLength, dwMemoryLoad; public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual; }
        [StructLayout(LayoutKind.Sequential)] struct SPS { public byte AC, Flag, Pct, Res1; public uint Life, Full; }
        [StructLayout(LayoutKind.Sequential)] struct FT { public uint lo, hi; }
        [DllImport("user32.dll")] static extern bool GetLastInputInfo(ref LASTINPUTINFO p);
        [DllImport("kernel32.dll")] static extern bool GlobalMemoryStatusEx(ref MEMSTAT m);
        [DllImport("kernel32.dll")] static extern bool GetSystemPowerStatus(out SPS s);
        [DllImport("kernel32.dll")] static extern bool GetSystemTimes(out FT idle, out FT kernel, out FT user);
        [DllImport("kernel32.dll")] public static extern IntPtr GetCurrentThread();
        [DllImport("kernel32.dll")] public static extern bool SetThreadPriority(IntPtr h, int p);
        [DllImport("kernel32.dll")] public static extern IntPtr OpenProcess(uint acc, bool inh, uint pid);
        [DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr h);
        [DllImport("psapi.dll")] public static extern bool EmptyWorkingSet(IntPtr h);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        public static int IdleSeconds()
        {
            LASTINPUTINFO li = new LASTINPUTINFO(); li.cbSize = (uint)Marshal.SizeOf(typeof(LASTINPUTINFO));
            if (!GetLastInputInfo(ref li)) return 0;
            return (int)(((uint)Environment.TickCount - li.dwTime) / 1000);
        }
        public static int MemLoad(out long totalMB, out long freeMB)
        {
            MEMSTAT m = new MEMSTAT(); m.dwLength = (uint)Marshal.SizeOf(typeof(MEMSTAT)); totalMB = 0; freeMB = 0;
            if (!GlobalMemoryStatusEx(ref m)) return 0; totalMB = (long)(m.ullTotalPhys / 1048576); freeMB = (long)(m.ullAvailPhys / 1048576); return (int)m.dwMemoryLoad;
        }
        public static bool OnLowBattery()
        {
            SPS s; if (!GetSystemPowerStatus(out s)) return false;
            return s.AC == 0 && s.Pct != 255 && s.Pct < 30;
        }
        static ulong T(FT f) { return ((ulong)f.hi << 32) | f.lo; }
        public static int CpuPercent(int sampleMs)
        {
            FT i1, k1, u1, i2, k2, u2;
            if (!GetSystemTimes(out i1, out k1, out u1)) return 0;
            Thread.Sleep(sampleMs);
            if (!GetSystemTimes(out i2, out k2, out u2)) return 0;
            ulong idle = T(i2) - T(i1), tot = (T(k2) - T(k1)) + (T(u2) - T(u1));
            return tot == 0 ? 0 : (int)(100 - (idle * 100 / tot));
        }
        [DllImport("kernel32.dll")] static extern ulong GetTickCount64();
        public static double UptimeMs() { return (double)GetTickCount64(); }
        public static uint ForegroundPid() { uint p; GetWindowThreadProcessId(GetForegroundWindow(), out p); return p; }
    }

    // ================= Job engine =================
    class JobResult { public long Bytes; public int Files; public string Note = ""; }
    class Job
    {
        public string Id, Name, Desc; public double Hours; public Func<int, JobResult> Run;
        public Job(string id, string name, string desc, double hours, Func<int, JobResult> run) { Id = id; Name = name; Desc = desc; Hours = hours; Run = run; }
    }

    static class Jobs
    {
        public static List<Job> All = new List<Job>();
        static int[] TempDays = { 7, 3, 1 };
        static int[] BinDays = { 30, 7, 1 };
        static long[] RamMB = { 200, 100, 50 };
        static Jobs()
        {
            All.Add(new Job("temp", "Temp and junk files", "Deletes old files in your Temp folder, crash dumps, error reports and the internet cache. Never touches Documents, Desktop or Downloads.", 24, Temp));
            All.Add(new Job("browser", "Browser caches", "Clears cache only (not passwords, history or logins) for Chrome, Edge, Brave and Firefox. Skips a browser while it is running.", 72, Browsers));
            All.Add(new Job("bin", "Recycle Bin", "Permanently deletes items that have been in the Recycle Bin longer than the limit (30 / 7 / 1 days by level).", 24, Recycle));
            All.Add(new Job("ram", "Free RAM", "Asks Windows to return unused memory from big background programs (never the program you are using). Also closes apps from your auto-close list, politely, when you are away.", 0.5, Ram));
            All.Add(new Job("caches", "System caches", "Flushes the DNS cache. At Aggressive level also clears Explorer thumbnail caches (they rebuild by themselves).", 48, Caches));
            All.Add(new Job("health", "Health check", "Looks for problems (low disk space, long uptime, high memory use, many startup apps) and writes advice to the log. Changes nothing.", 12, Health));
        }
        public static string Mb(long b) { return (b / 1048576.0).ToString("0.0") + " MB"; }

        // ---- helpers ----
        public static JobResult CleanDir(string root, int ageDays, bool removeEmptyDirs)
        {
            JobResult r = new JobResult(); if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return r;
            DateTime cut = DateTime.Now.AddDays(-ageDays);
            Stack<string> st = new Stack<string>(); st.Push(root); List<string> dirs = new List<string>();
            while (st.Count > 0)
            {
                string d = st.Pop();
                try
                {
                    foreach (string sub in Directory.GetDirectories(d))
                    {
                        try { if ((File.GetAttributes(sub) & FileAttributes.ReparsePoint) != 0) continue; } catch { continue; }
                        st.Push(sub); dirs.Add(sub);
                    }
                }
                catch { }
                try
                {
                    foreach (string f in Directory.GetFiles(d))
                    {
                        try
                        {
                            FileInfo fi = new FileInfo(f);
                            if (fi.LastWriteTime > cut || fi.CreationTime > cut) continue;
                            long len = fi.Length; fi.Attributes = FileAttributes.Normal; fi.Delete(); r.Bytes += len; r.Files++;
                        }
                        catch { }
                    }
                }
                catch { }
            }
            if (removeEmptyDirs) for (int i = dirs.Count - 1; i >= 0; i--) { try { Directory.Delete(dirs[i]); } catch { } }
            return r;
        }
        static long DirSize(string d)
        {
            long s = 0;
            try { foreach (string f in Directory.GetFiles(d)) { try { s += new FileInfo(f).Length; } catch { } } foreach (string sd in Directory.GetDirectories(d)) s += DirSize(sd); } catch { }
            return s;
        }
        static void ForceDeleteDir(string d)
        {
            try { foreach (string f in Directory.GetFiles(d)) { try { File.SetAttributes(f, FileAttributes.Normal); File.Delete(f); } catch { } } foreach (string sd in Directory.GetDirectories(d)) ForceDeleteDir(sd); Directory.Delete(d); } catch { }
        }
        static void Add(JobResult a, JobResult b) { a.Bytes += b.Bytes; a.Files += b.Files; }
        static bool Running(params string[] names) { foreach (string n in names) { try { Process[] p = Process.GetProcessesByName(n); bool any = p.Length > 0; foreach (Process x in p) x.Dispose(); if (any) return true; } catch { } } return false; }
        static string LocalApp { get { return Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData); } }

        // ---- jobs ----
        static JobResult Temp(int level)
        {
            JobResult r = new JobResult(); int age = TempDays[level];
            Add(r, CleanDir(Path.GetTempPath(), age, true));
            Add(r, CleanDir(Path.Combine(LocalApp, "CrashDumps"), age, true));
            Add(r, CleanDir(Path.Combine(LocalApp, "Microsoft", "Windows", "WER"), age, true));
            Add(r, CleanDir(Path.Combine(LocalApp, "Microsoft", "Windows", "INetCache"), age, true));
            Add(r, CleanDir(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp"), age, true));
            return r;
        }
        static JobResult Browsers(int level)
        {
            JobResult r = new JobResult(); int age = TempDays[level]; List<string> skipped = new List<string>();
            string[][] chromium = {
                new string[] { Path.Combine(LocalApp, "Google", "Chrome", "User Data"), "chrome" },
                new string[] { Path.Combine(LocalApp, "Microsoft", "Edge", "User Data"), "msedge" },
                new string[] { Path.Combine(LocalApp, "BraveSoftware", "Brave-Browser", "User Data"), "brave" } };
            foreach (string[] c in chromium)
            {
                if (!Directory.Exists(c[0])) continue;
                if (Running(c[1])) { skipped.Add(c[1]); continue; }
                try
                {
                    foreach (string prof in Directory.GetDirectories(c[0]))
                    {
                        string pn = Path.GetFileName(prof);
                        if (pn != "Default" && !pn.StartsWith("Profile ")) continue;
                        foreach (string sub in new string[] { "Cache", "Code Cache", "GPUCache" }) Add(r, CleanDir(Path.Combine(prof, sub), age, true));
                    }
                }
                catch { }
            }
            string ff = Path.Combine(LocalApp, "Mozilla", "Firefox", "Profiles");
            if (Directory.Exists(ff))
            {
                if (Running("firefox")) skipped.Add("firefox");
                else { try { foreach (string prof in Directory.GetDirectories(ff)) Add(r, CleanDir(Path.Combine(prof, "cache2"), age, true)); } catch { } }
            }
            if (skipped.Count > 0) r.Note = "skipped (running): " + string.Join(", ", skipped.ToArray());
            return r;
        }
        static JobResult Recycle(int level)
        {
            JobResult r = new JobResult(); DateTime cut = DateTime.Now.AddDays(-BinDays[level]);
            string sid = WindowsIdentity.GetCurrent().User.Value;
            foreach (DriveInfo di in DriveInfo.GetDrives())
            {
                try
                {
                    if (di.DriveType != DriveType.Fixed || !di.IsReady) continue;
                    string rb = Path.Combine(di.Name, "$Recycle.Bin", sid); if (!Directory.Exists(rb)) continue;
                    foreach (string i in Directory.GetFiles(rb, "$I*"))
                    {
                        try
                        {
                            FileInfo fi = new FileInfo(i); if (fi.CreationTime > cut && fi.LastWriteTime > cut) continue;
                            string rp = Path.Combine(rb, "$R" + fi.Name.Substring(2)); long sz = 0;
                            if (Directory.Exists(rp)) { sz = DirSize(rp); ForceDeleteDir(rp); if (Directory.Exists(rp)) continue; }
                            else if (File.Exists(rp)) { sz = new FileInfo(rp).Length; File.SetAttributes(rp, FileAttributes.Normal); File.Delete(rp); }
                            fi.Attributes = FileAttributes.Normal; fi.Delete(); r.Bytes += sz; r.Files++;
                        }
                        catch { }
                    }
                }
                catch { }
            }
            return r;
        }
        static JobResult Ram(int level)
        {
            JobResult r = new JobResult(); long min = RamMB[level] * 1048576; uint fg = N.ForegroundPid(); int me = Process.GetCurrentProcess().Id;
            HashSet<string> skip = new HashSet<string>(new string[] { "system", "idle", "dwm", "explorer", "csrss", "winlogon", "services", "lsass", "smss", "wininit", "searchhost", "startmenuexperiencehost", "shellexperiencehost", "textinputhost", "audiodg", "memcompression", "registry", "sihost", "taskmgr", "ctfmon" });
            int n = 0; long freed = 0; List<string> top = new List<string>();
            foreach (Process p in Process.GetProcesses())
            {
                try
                {
                    if (p.Id == me || p.Id == (int)fg || p.SessionId == 0) continue;
                    string nm = p.ProcessName.ToLowerInvariant(); if (skip.Contains(nm)) continue;
                    long before = p.WorkingSet64; if (before < min) continue;
                    IntPtr h = N.OpenProcess(0x0100 | 0x0400, false, (uint)p.Id); if (h == IntPtr.Zero) continue;
                    N.EmptyWorkingSet(h); N.CloseHandle(h); p.Refresh(); long after = p.WorkingSet64;
                    if (before > after) { freed += before - after; n++; }
                }
                catch { }
                finally { p.Dispose(); }
            }
            r.Bytes = freed; r.Files = n; r.Note = "trimmed " + n + " background programs";
            string list = Cfg.S("autoclose", "").Trim();
            if (list != "" && N.IdleSeconds() >= 600)
            {
                foreach (string raw in list.Split(','))
                {
                    string nm = raw.Trim().ToLowerInvariant(); if (nm.EndsWith(".exe")) nm = nm.Substring(0, nm.Length - 4); if (nm == "" || skip.Contains(nm)) continue;
                    try { foreach (Process p in Process.GetProcessesByName(nm)) { try { if (p.MainWindowHandle != IntPtr.Zero && p.CloseMainWindow()) Log.Add("asked " + nm + " to close (auto-close list)"); } catch { } p.Dispose(); } } catch { }
                }
            }
            return r;
        }
        static void Sh(string cmd, int ms)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo("cmd.exe", "/c " + cmd); psi.CreateNoWindow = true; psi.UseShellExecute = false; psi.WindowStyle = ProcessWindowStyle.Hidden;
                using (Process p = Process.Start(psi)) { if (!p.WaitForExit(ms)) { try { p.Kill(); } catch { } } }
            }
            catch { }
        }
        static JobResult Caches(int level)
        {
            JobResult r = new JobResult(); Sh("ipconfig /flushdns", 15000); r.Note = "DNS cache flushed";
            if (level >= 2)
            {
                try
                {
                    string ex = Path.Combine(LocalApp, "Microsoft", "Windows", "Explorer");
                    foreach (string pat in new string[] { "thumbcache_*.db", "iconcache_*.db" })
                        foreach (string f in Directory.GetFiles(ex, pat)) { try { long l = new FileInfo(f).Length; File.Delete(f); r.Bytes += l; r.Files++; } catch { } }
                    r.Note += ", thumbnail caches cleared";
                }
                catch { }
            }
            return r;
        }
        static JobResult Health(int level)
        {
            JobResult r = new JobResult(); List<string> notes = new List<string>();
            foreach (DriveInfo di in DriveInfo.GetDrives())
            {
                try { if (di.DriveType == DriveType.Fixed && di.IsReady) { double pct = di.AvailableFreeSpace * 100.0 / di.TotalSize; if (pct < 10) notes.Add("drive " + di.Name + " has only " + pct.ToString("0") + "% free"); } } catch { }
            }
            double up = TimeSpan.FromMilliseconds(N.UptimeMs()).TotalDays; if (up > 10) notes.Add("PC has been on for " + up.ToString("0") + " days, a restart helps");
            long tot, fr; int load = N.MemLoad(out tot, out fr); if (load > 85) notes.Add("memory use is " + load + "%, close heavy programs or restart");
            int startup = 0; try { using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run")) { if (k != null) startup = k.GetValueNames().Length; } } catch { }
            if (startup > 12) notes.Add(startup + " startup apps (user level), trim them in Settings > Apps > Startup");
            r.Note = notes.Count == 0 ? "all good (memory " + load + "%)" : string.Join("; ", notes.ToArray());
            return r;
        }

        // ---- deep clean (admin) ----
        public static void Deep()
        {
            Log.Add("Deep clean started (administrator)"); long total = 0;
            string win = Environment.GetFolderPath(Environment.SpecialFolder.Windows), pd = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            total += CleanDir(Path.Combine(win, "Temp"), 1, true).Bytes;
            Sh("net stop wuauserv", 60000); Sh("net stop bits", 60000);
            total += CleanDir(Path.Combine(win, "SoftwareDistribution", "Download"), 0, true).Bytes;
            Sh("net start bits", 60000); Sh("net start wuauserv", 60000);
            total += CleanDir(Path.Combine(pd, "Microsoft", "Windows", "WER", "ReportArchive"), 3, true).Bytes;
            total += CleanDir(Path.Combine(pd, "Microsoft", "Windows", "WER", "ReportQueue"), 3, true).Bytes;
            total += CleanDir(Path.Combine(win, "ServiceProfiles", "NetworkService", "AppData", "Local", "Microsoft", "Windows", "DeliveryOptimization", "Cache"), 1, true).Bytes;
            Log.Add("Deep clean: removed " + Mb(total) + ". Running component cleanup...");
            Sh("dism.exe /online /Cleanup-Image /StartComponentCleanup", 1800000);
            Log.Add("Deep clean finished");
        }
    }


    // ================= Scheduler =================
    static class Engine
    {
        static volatile bool busy; public static volatile bool Paused;
        public static bool Busy { get { return busy; } }
        public static int Level { get { return Math.Max(0, Math.Min(2, Cfg.I("level", 1))); } }
        public static bool JobOn(Job j) { return Cfg.B("job." + j.Id + ".on", true); }
        public static double JobHours(Job j) { int h = Cfg.I("job." + j.Id + ".hours10", (int)(j.Hours * 10)); return h / 10.0; }
        public static void Tick()
        {
            if (busy || Paused) return;
            List<Job> due = new List<Job>(); DateTime now = DateTime.Now;
            foreach (Job j in Jobs.All)
            {
                if (!JobOn(j)) continue;
                long last = Cfg.L("job." + j.Id + ".last", 0);
                if (last == 0 || (now - new DateTime(last)).TotalHours >= JobHours(j)) due.Add(j);
            }
            if (due.Count == 0) return;
            if (Cfg.B("idleonly", true) && N.IdleSeconds() < 90) return;
            if (N.OnLowBattery()) return;
            RunAsync(due, false);
        }
        public static void RunAsync(List<Job> list, bool manual)
        {
            if (busy) return; busy = true;
            Thread t = new Thread(delegate ()
            {
                try
                {
                    try { N.SetThreadPriority(N.GetCurrentThread(), 0x00010000); } catch { }
                    foreach (Job j in list)
                    {
                        if (!manual && Cfg.B("idleonly", true) && N.IdleSeconds() < 20) break;
                        if (!manual && N.CpuPercent(500) > 60) { Thread.Sleep(5000); if (N.CpuPercent(500) > 60) continue; }
                        RunOne(j);
                        Thread.Sleep(300);
                    }
                }
                catch (Exception ex) { Log.Add("ERROR " + ex); }
                finally { busy = false; Cfg.Save(); }
            });
            t.IsBackground = true; t.Name = "PCCareWorker"; t.Start();
        }
        static void RunOne(Job j)
        {
            try
            {
                JobResult r = j.Run(Level);
                Cfg.Set("job." + j.Id + ".last", DateTime.Now.Ticks.ToString());
                Cfg.Set("stat.bytes", (Cfg.L("stat.bytes", 0) + r.Bytes).ToString());
                Log.Add(j.Name + ": " + (r.Bytes > 0 ? "freed " + Jobs.Mb(r.Bytes) : "nothing to do") + (r.Files > 0 ? " (" + r.Files + " items)" : "") + (r.Note != "" ? " - " + r.Note : ""));
                if (j.Id == "health" && r.Note.IndexOf("all good") < 0 && Cfg.B("notify", false) && Program.Tray != null) { Program.Tray.ShowBalloonTip(5000, "PC Care", r.Note, ToolTipIcon.Info); }
            }
            catch (Exception ex) { Log.Add(j.Name + ": error " + ex.Message); }
        }
    }

    // ================= Tweaks =================
    class Tweak
    {
        public string Id, Name, Desc, Path, Value; public RegistryValueKind Kind; public object Val;
        public Tweak(string id, string name, string desc, string path, string value, RegistryValueKind kind, object val) { Id = id; Name = name; Desc = desc; Path = path; Value = value; Kind = kind; Val = val; }
        public bool On { get { return Cfg.B("tw." + Id + ".on", false); } }
        public void Apply()
        {
            if (On) return;
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(Path))
            {
                object prev = k.GetValue(Value); Cfg.Set("tw." + Id + ".prev", prev == null ? "<none>" : prev.ToString());
                k.SetValue(Value, Val, Kind);
            }
            Cfg.Set("tw." + Id + ".on", true); Cfg.Save(); Log.Add("Tweak applied: " + Name);
        }
        public void Undo()
        {
            if (!On) return;
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(Path))
            {
                string prev = Cfg.S("tw." + Id + ".prev", "<none>");
                if (prev == "<none>") k.DeleteValue(Value, false);
                else if (Kind == RegistryValueKind.DWord) k.SetValue(Value, int.Parse(prev), Kind);
                else k.SetValue(Value, prev, Kind);
            }
            Cfg.Set("tw." + Id + ".on", false); Cfg.Save(); Log.Add("Tweak undone: " + Name);
        }
        public static List<Tweak> All = new List<Tweak> {
            new Tweak("menudelay", "Instant menus", "Removes the 400 ms wait before sub-menus open. Takes effect after sign-out.", @"Control Panel\Desktop", "MenuShowDelay", RegistryValueKind.String, "0"),
            new Tweak("startupdelay", "No delay for startup apps", "Windows waits several seconds before launching your startup apps. This removes the wait, so the desktop is usable sooner after login.", @"Software\Microsoft\Windows\CurrentVersion\Explorer\Serialize", "StartupDelayInMSec", RegistryValueKind.DWord, 0),
            new Tweak("websearch", "Faster Start search (no web results)", "Turns off Bing web suggestions in Start search, so searching is faster and stays local. Takes effect after sign-out.", @"Software\Policies\Microsoft\Windows\Explorer", "DisableSearchBoxSuggestions", RegistryValueKind.DWord, 1)
        };
    }

    // ================= Settings window =================
    class SettingsForm : Form
    {
        static SettingsForm inst; TextBox logBox; Label totalLbl; System.Windows.Forms.Timer refresh = new System.Windows.Forms.Timer(); Dictionary<string, Label> lastLbl = new Dictionary<string, Label>();
        public static void ShowIt() { if (inst != null && !inst.IsDisposed) { inst.Activate(); return; } inst = new SettingsForm(); inst.Show(); }
        static string Ago(long ticks) { if (ticks == 0) return "never run"; TimeSpan t = DateTime.Now - new DateTime(ticks); return t.TotalMinutes < 1 ? "just now" : (t.TotalHours < 1 ? (int)t.TotalMinutes + " min ago" : (t.TotalDays < 1 ? (int)t.TotalHours + " h ago" : (int)t.TotalDays + " days ago")); }
        public SettingsForm()
        {
            Text = "PC Care - automatic cleaning and speed"; Width = 760; Height = 700; StartPosition = FormStartPosition.CenterScreen;
            TabControl tabs = new TabControl(); tabs.Dock = DockStyle.Fill;
            tabs.TabPages.Add(JobsTab()); tabs.TabPages.Add(TweaksTab()); tabs.TabPages.Add(ToolsTab()); tabs.TabPages.Add(LogTab());
            tabs.SelectedIndexChanged += delegate { LoadLog(); };
            Controls.Add(tabs);
            refresh.Interval = 3000; refresh.Tick += delegate { UpdateStatus(); }; refresh.Start();
            FormClosed += delegate { refresh.Stop(); refresh.Dispose(); Cfg.Save(); };
        }
        void UpdateStatus()
        {
            try
            {
                if (totalLbl != null) totalLbl.Text = "Total freed so far: " + Jobs.Mb(Cfg.L("stat.bytes", 0)) + (Engine.Busy ? "   (working now...)" : "") + (Engine.Paused ? "   (PAUSED)" : "");
                foreach (Job j in Jobs.All) { Label l; if (lastLbl.TryGetValue(j.Id, out l)) l.Text = "last: " + Ago(Cfg.L("job." + j.Id + ".last", 0)); }
            }
            catch { }
        }
        TabPage JobsTab()
        {
            TabPage tp = new TabPage("Jobs"); tp.AutoScroll = true; int y = 8;
            totalLbl = new Label(); totalLbl.Left = 8; totalLbl.Top = y; totalLbl.Width = 700; totalLbl.Font = new Font(Font, FontStyle.Bold); tp.Controls.Add(totalLbl); y += 28;
            Label l1 = new Label(); l1.Text = "Aggressiveness:"; l1.Left = 8; l1.Top = y + 4; l1.AutoSize = true; tp.Controls.Add(l1);
            ComboBox lv = new ComboBox(); lv.DropDownStyle = ComboBoxStyle.DropDownList; lv.Left = 110; lv.Top = y; lv.Width = 330;
            lv.Items.AddRange(new object[] { "Gentle (files older than 7 days, bin 30 days)", "Balanced (3 days, bin 7 days)", "Aggressive (1 day, bin 1 day)" });
            lv.SelectedIndex = Engine.Level; lv.SelectedIndexChanged += delegate { Cfg.Set("level", lv.SelectedIndex.ToString()); Cfg.Save(); }; tp.Controls.Add(lv); y += 34;
            CheckBox io = new CheckBox(); io.Text = "Only work while I am away from the PC (no input for 90 s) and the CPU is quiet"; io.Left = 8; io.Top = y; io.Width = 700; io.Checked = Cfg.B("idleonly", true);
            io.CheckedChanged += delegate { Cfg.Set("idleonly", io.Checked); Cfg.Save(); }; tp.Controls.Add(io); y += 28;
            CheckBox nt = new CheckBox(); nt.Text = "Show a small notification when the health check finds a problem (off = completely silent)"; nt.Left = 8; nt.Top = y; nt.Width = 700; nt.Checked = Cfg.B("notify", false);
            nt.CheckedChanged += delegate { Cfg.Set("notify", nt.Checked); Cfg.Save(); }; tp.Controls.Add(nt); y += 34;
            foreach (Job j in Jobs.All)
            {
                Job jj = j;
                CheckBox cb = new CheckBox(); cb.Text = j.Name; cb.Left = 8; cb.Top = y; cb.Width = 200; cb.Font = new Font(Font, FontStyle.Bold); cb.Checked = Engine.JobOn(j);
                cb.CheckedChanged += delegate { Cfg.Set("job." + jj.Id + ".on", cb.Checked); Cfg.Save(); }; tp.Controls.Add(cb);
                Label ev = new Label(); ev.Text = "every (hours):"; ev.Left = 230; ev.Top = y + 4; ev.AutoSize = true; tp.Controls.Add(ev);
                NumericUpDown nu = new NumericUpDown(); nu.Left = 320; nu.Top = y; nu.Width = 70; nu.DecimalPlaces = 1; nu.Minimum = 0.5m; nu.Maximum = 720; nu.Increment = 0.5m;
                nu.Value = Math.Max(0.5m, Math.Min(720m, (decimal)Engine.JobHours(j)));
                nu.ValueChanged += delegate { Cfg.Set("job." + jj.Id + ".hours10", ((int)(nu.Value * 10)).ToString()); Cfg.Save(); }; tp.Controls.Add(nu);
                Button rn = new Button(); rn.Text = "Run now"; rn.Left = 410; rn.Top = y - 1; rn.Width = 80; rn.Height = 26;
                rn.Click += delegate { if (Engine.Busy) { MessageBox.Show("Already working, try again in a moment."); return; } List<Job> one = new List<Job>(); one.Add(jj); Engine.RunAsync(one, true); }; tp.Controls.Add(rn);
                Label ls = new Label(); ls.Left = 505; ls.Top = y + 4; ls.Width = 200; ls.Text = "last: " + Ago(Cfg.L("job." + j.Id + ".last", 0)); lastLbl[j.Id] = ls; tp.Controls.Add(ls); y += 28;
                Label ds = new Label(); ds.Text = j.Desc; ds.Left = 28; ds.Top = y; ds.Width = 690; ds.Height = 34; ds.ForeColor = Color.DimGray; tp.Controls.Add(ds); y += 40;
            }
            Label ac = new Label(); ac.Text = "Auto-close list (optional): program names, separated by commas, for example  onenote, teams.  They are closed politely (like clicking X, never force-killed) only when you are away for 10+ minutes."; ac.Left = 8; ac.Top = y; ac.Width = 700; ac.Height = 34; tp.Controls.Add(ac); y += 36;
            TextBox at = new TextBox(); at.Left = 8; at.Top = y; at.Width = 700; at.Text = Cfg.S("autoclose", ""); at.TextChanged += delegate { Cfg.Set("autoclose", at.Text); Cfg.Save(); }; tp.Controls.Add(at); y += 36;
            Button all = new Button(); all.Text = "Run all jobs now"; all.Left = 8; all.Top = y; all.Width = 160; all.Height = 30;
            all.Click += delegate { if (Engine.Busy) return; List<Job> l = new List<Job>(); foreach (Job j in Jobs.All) if (Engine.JobOn(j)) l.Add(j); Engine.RunAsync(l, true); }; tp.Controls.Add(all);
            Button ps = new Button(); ps.Text = Engine.Paused ? "Resume" : "Pause all"; ps.Left = 180; ps.Top = y; ps.Width = 120; ps.Height = 30;
            ps.Click += delegate { Engine.Paused = !Engine.Paused; ps.Text = Engine.Paused ? "Resume" : "Pause all"; }; tp.Controls.Add(ps);
            CheckBox au = new CheckBox(); au.Text = "Start with Windows"; au.Left = 320; au.Top = y + 5; au.Width = 200; au.Checked = Program.IsAutostart(); au.CheckedChanged += delegate { Program.SetAutostart(au.Checked); }; tp.Controls.Add(au);
            UpdateStatus(); return tp;
        }
        TabPage TweaksTab()
        {
            TabPage tp = new TabPage("Speed tweaks"); int y = 10;
            Label h = new Label(); h.Text = "Small, reversible Windows settings (current user only). Each one remembers your old value and can be undone."; h.Left = 8; h.Top = y; h.Width = 700; h.Height = 34; tp.Controls.Add(h); y += 40;
            foreach (Tweak t in Tweak.All)
            {
                Tweak tt = t; CheckBox cb = new CheckBox(); cb.Text = t.Name; cb.Left = 8; cb.Top = y; cb.Width = 500; cb.Font = new Font(Font, FontStyle.Bold); cb.Checked = t.On;
                cb.CheckedChanged += delegate { try { if (cb.Checked) tt.Apply(); else tt.Undo(); } catch (Exception ex) { Log.Add("Tweak error: " + ex.Message); } }; tp.Controls.Add(cb); y += 26;
                Label d = new Label(); d.Text = t.Desc; d.Left = 28; d.Top = y; d.Width = 690; d.Height = 34; d.ForeColor = Color.DimGray; tp.Controls.Add(d); y += 46;
            }
            return tp;
        }
        static void Admin(string file, string args)
        {
            try { ProcessStartInfo psi = new ProcessStartInfo(file, args); psi.UseShellExecute = true; psi.Verb = "runas"; Process.Start(psi); }
            catch (Exception ex) { Log.Add("Could not start " + file + ": " + ex.Message); }
        }
        TabPage ToolsTab()
        {
            TabPage tp = new TabPage("Repair tools"); int y = 10;
            Label h = new Label(); h.Text = "These need administrator rights, so Windows asks once each time. They run only when you press them."; h.Left = 8; h.Top = y; h.Width = 700; h.Height = 30; tp.Controls.Add(h); y += 36;
            Action<string, string, Action> mk = delegate (string t, string d, Action a)
            {
                Button b = new Button(); b.Text = t; b.Left = 8; b.Top = y; b.Width = 300; b.Height = 30; b.Click += delegate { try { a(); } catch (Exception ex) { Log.Add("Tool error: " + ex.Message); } }; tp.Controls.Add(b);
                Label l = new Label(); l.Text = d; l.Left = 320; l.Top = y; l.Width = 400; l.Height = 44; l.ForeColor = Color.DimGray; tp.Controls.Add(l); y += 52;
            };
            mk("Deep clean (Windows temp, update cache)", "Cleans system-level junk that normal cleaning cannot reach: Windows\\Temp, downloaded update files, error reports, then Windows component cleanup. Can take several minutes, runs hidden.", delegate { Admin(Application.ExecutablePath, "--deep"); });
            mk("Repair Windows files (DISM + SFC)", "Repairs corrupted Windows system files. Opens a console window and takes 10 to 30 minutes. Safe to run, it only replaces damaged system files.", delegate { Admin("cmd.exe", "/k \"dism /online /cleanup-image /restorehealth & sfc /scannow\""); });
            mk("Check the disk for errors (read-only scan)", "Scans the system drive for file-system problems without changing anything.", delegate { Admin("cmd.exe", "/k \"chkdsk C: /scan\""); });
            mk("Test the RAM for hardware faults", "Opens Windows Memory Diagnostic. It needs a restart and takes a few minutes. Software cannot fix bad RAM, this only finds it.", delegate { Admin("mdsched.exe", ""); });
            mk("Restart Explorer (refresh taskbar and Start)", "Restarts the Windows shell. The taskbar flickers for a second. Open windows stay open. Helps when Start or the taskbar is slow or stuck.", delegate
            {
                foreach (Process p in Process.GetProcessesByName("explorer")) { try { p.Kill(); } catch { } p.Dispose(); }
                Log.Add("Explorer restarted");
            });
            mk("Open Startup apps", "Shows the list of programs that start with Windows, with their impact. Switch off what you don't need.", delegate { Process.Start("ms-settings:startupapps"); });
            return tp;
        }
        TabPage LogTab()
        {
            TabPage tp = new TabPage("Log"); logBox = new TextBox(); logBox.Multiline = true; logBox.ReadOnly = true; logBox.ScrollBars = ScrollBars.Vertical; logBox.Dock = DockStyle.Fill; logBox.Font = new Font("Consolas", 9f);
            Panel p = new Panel(); p.Dock = DockStyle.Top; p.Height = 36;
            Button r = new Button(); r.Text = "Refresh"; r.Left = 8; r.Top = 4; r.Width = 90; r.Click += delegate { LoadLog(); }; p.Controls.Add(r);
            Button o = new Button(); o.Text = "Open log file"; o.Left = 106; o.Top = 4; o.Width = 110; o.Click += delegate { try { if (!File.Exists(Log.PathOf)) File.WriteAllText(Log.PathOf, ""); Process.Start("notepad.exe", "\"" + Log.PathOf + "\""); } catch { } }; p.Controls.Add(o);
            tp.Controls.Add(logBox); tp.Controls.Add(p); LoadLog(); return tp;
        }
        void LoadLog() { if (logBox != null) logBox.Text = Log.Tail(300); }
    }

    // ================= Program =================
    static class Program
    {
        public static NotifyIcon Tray; static System.Windows.Forms.Timer tick;
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        public static bool IsAutostart() { try { using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey)) return k != null && k.GetValue("PCCare") != null; } catch { return false; } }
                static void Approve()
        {
            try { using (RegistryKey k = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run")) k.SetValue("PCCare", new byte[] { 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, RegistryValueKind.Binary); } catch { }
        }
        public static void AutoDiag()
        {
            try
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PCCare"); Directory.CreateDirectory(dir);
                object run = null, ap = null;
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run")) { if (k != null) run = k.GetValue("PCCare"); }
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run")) { if (k != null) ap = k.GetValue("PCCare"); }
                byte[] b = ap as byte[]; string st = b == null ? "none" : BitConverter.ToString(b);
                string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  started; uptime " + ((uint)Environment.TickCount / 60000) + " min; exe=" + Application.ExecutablePath + "; Run=" + (run == null ? "missing" : "present") + "; StartupApproved=" + st + (b != null && b.Length > 0 && (b[0] & 1) == 1 ? " (DISABLED by Windows)" : "") + "\r\n";
                string f = Path.Combine(dir, "startup.log"); try { if (File.Exists(f) && new FileInfo(f).Length > 50000) File.Delete(f); } catch { }
                File.AppendAllText(f, line);
            }
            catch { }
        }
        public static void SetAutostart(bool on) { SetAutostartRaw(on); if (on) Approve(); }
        static void SetAutostartRaw(bool on)
        {
            try { using (RegistryKey k = Registry.CurrentUser.CreateSubKey(RunKey)) { if (on) k.SetValue("PCCare", "\"" + Application.ExecutablePath + "\""); else k.DeleteValue("PCCare", false); } } catch { }
        }
        [STAThread]
        static void Main(string[] args)
        {
            Crash.Install();
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            if (args.Length > 0 && args[0] == "--deep") { try { Jobs.Deep(); } catch (Exception ex) { Log.Add("ERROR " + ex); } return; }
            bool created; Mutex mx = new Mutex(true, "PCCare_single_instance", out created); if (!created) return;
            Cfg.Load();
            bool first = !Cfg.B("init", false);
            AutoDiag(); if (!first && IsAutostart()) SetAutostart(true);
            if (first) { SetAutostart(true); Cfg.Set("init", true); Cfg.Save(); }
            Tray = new NotifyIcon(); Tray.Icon = SystemIcons.Shield; Tray.Text = "PC Care (working quietly)"; Tray.Visible = true;
            ContextMenuStrip m = new ContextMenuStrip();
            m.Items.Add("Open PC Care", null, delegate { SettingsForm.ShowIt(); });
            m.Items.Add("Run all jobs now", null, delegate { if (Engine.Busy) return; List<Job> l = new List<Job>(); foreach (Job j in Jobs.All) if (Engine.JobOn(j)) l.Add(j); Engine.RunAsync(l, true); });
            ToolStripMenuItem pz = new ToolStripMenuItem("Pause"); pz.Click += delegate { Engine.Paused = !Engine.Paused; pz.Text = Engine.Paused ? "Resume" : "Pause"; }; m.Items.Add(pz);
            m.Items.Add("Exit", null, delegate { Tray.Visible = false; Cfg.Save(); Application.Exit(); });
            Tray.ContextMenuStrip = m; Tray.DoubleClick += delegate { SettingsForm.ShowIt(); };
            Log.Add("PC Care started");
            if (first) { Tray.ShowBalloonTip(6000, "PC Care is running", "It cleans and speeds up your PC quietly in the background. Open it from the tray icon.", ToolTipIcon.Info); SettingsForm.ShowIt(); }
            tick = new System.Windows.Forms.Timer(); tick.Interval = 60000; tick.Tick += delegate { try { Engine.Tick(); } catch (Exception ex) { Log.Add("ERROR " + ex); } };
            System.Windows.Forms.Timer first1 = new System.Windows.Forms.Timer(); first1.Interval = 45000; first1.Tick += delegate { first1.Stop(); try { Engine.Tick(); } catch { } tick.Start(); }; first1.Start();
            Application.Run();
            GC.KeepAlive(mx);
        }
    }
}

