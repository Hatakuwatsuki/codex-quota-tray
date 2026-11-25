// ============================================================
//  Codex 额度显示器（Windows 通知区域 / 托盘）
//
//  显示四样东西：5 小时额度、7 天额度，以及两者各自什么时候恢复。
//  悬停图标看数字；右键菜单：刷新 / 额度详情 / 额度提醒 / 开机自启动 / 建快捷方式 / 退出。
//  注意：网关模式不随正式版发布（见项目书 2.0），切模式用桌面上的独立脚本。
//
//  数据来源：quota.js -> codex app-server -> account/rateLimits/read
//  编译：csc /target:winexe（见 rebuild.ps1）
// ============================================================
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

[assembly: AssemblyTitle("Codex Quota Tray")]
[assembly: AssemblyProduct("Codex Quota Tray")]
[assembly: AssemblyDescription("Shows ChatGPT / Codex 5-hour and 7-day quota in the notification area")]
[assembly: AssemblyVersion("1.9.58.0")]

namespace QuotaTrayApp
{
    internal static class Program
    {
        internal static string SettingsRequestName(string[] args)
        {
            string arg = args == null || args.Length == 0 ? "--settings" : args[0];
            switch (arg)
            {
                case "--settings": return "CodexQuotaTraySettingsRequest";
                case "--display-settings": return "CodexQuotaTrayDisplayRequest";
                case "--connect-accounts": return "CodexQuotaTrayAccountsRequest";
                case "--quota-details": return "CodexQuotaTrayDetailsRequest";
                case "--exit": return "CodexQuotaTrayExitRequest";
                case "--test-group-ui": return "CodexQuotaTrayGroupTestRequest";
                default: return null;
            }
        }
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint GetFileAttributes(string path);

        private static int CheckAccountPaths()
        {
            try
            {
                string[] paths;
                using (var input = new StreamReader(Console.OpenStandardInput(), Encoding.UTF8))
                    paths = new JavaScriptSerializer().Deserialize<string[]>(input.ReadToEnd());
                foreach (string path in paths)
                {
                    if (!Path.IsPathRooted(path)) return 4;
                    uint attrs = GetFileAttributes(path);
                    if (attrs == UInt32.MaxValue)
                    {
                        int error = Marshal.GetLastWin32Error();
                        if (error != 2 && error != 3) return 4;
                    }
                    else if ((attrs & (uint)FileAttributes.ReparsePoint) != 0) return 3;
                }
                return 0;
            }
            catch (Exception) { return 4; }
        }

        [DllImport("user32.dll")] private static extern IntPtr SetProcessDpiAwarenessContext(IntPtr value);
        [DllImport("user32.dll")] private static extern bool SetProcessDPIAware();
        internal static bool DpiAware;
        internal static void EnableDpiAwareness()
        {
            if (DpiAware) return;
            try { if (SetProcessDpiAwarenessContext(new IntPtr(-4)) != IntPtr.Zero) { DpiAware = true; return; } }
            catch (EntryPointNotFoundException) { }
            catch (DllNotFoundException) { }
            try { DpiAware = SetProcessDPIAware(); }
            catch { }
        }

        [STAThread]
        private static void Main(string[] args)
        {
#if QUOTA_TOAST_PREVIEW
            if (args.Length < 2 || args[0] != "--demo-toast") return;
#endif
            EnableDpiAwareness();
            if (args != null && args.Length == 1 && args[0] == "--check-account-paths")
            {
                Environment.ExitCode = CheckAccountPaths();
                return;
            }

            // 命令行模式：给构建脚本用，不启动托盘
            if (args != null && args.Length >= 1 && args[0] == "--test-menu")
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                UiTheme.RefreshSystem();
                L.AutoDetect();
                var popup = new UiPopup(new[] { L.T("刷新"), L.T("设置"), L.T("退出") }, 0, true,
                    UiTheme.UiFont(10.5f), delegate { }, 2, L.F("账号 {0}  |  {1}", 1, "Plus"));
                popup.Open(null, delegate { Point at = Cursor.Position; return new Rectangle(at.X - 24, at.Y - 24, 48, 48); }, 196, true);
                var preview = new System.Windows.Forms.Timer();
                preview.Interval = 6000;
                preview.Tick += delegate { preview.Stop(); popup.Close(); Application.ExitThread(); };
                preview.Start();
                Application.Run(popup);
                return;
            }
            if (args != null && args.Length >= 1 && args[0] == "--test-quota-tip")
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                UiTheme.RefreshSystem();
                L.AutoDetect();
                UiQuotaTip.ShowPreview("账号 1  |  Plus", new QuotaTipLine[] {
                    new QuotaTipLine { Label = L.T("5 小时"), Remaining = 91, Detail = L.T("1 小时 45 分后恢复"), Superseded = true },
                    new QuotaTipLine { Label = L.F("{0} 天", 7), Remaining = 0, Detail = L.T("18 小时 5 分后恢复") }
                }, 6000);
                return;
            }
            if (args != null && args.Length >= 2)
            {
                if (args[0] == "--make-ico")
                {
                    TrayContext.WriteIco(args[1], 0, 0, false);
                    return;
                }
                if (args[0] == "--icon-preview")
                {
                    // 用法：--icon-preview <目录> [上段已用%] [下段已用%]
                    // 不传就沿用 14 / 68。加这两个参数是为了能按指定状态做对比图。
                    double pu = 14, su = 68;
                    if (args.Length >= 4)
                    {
                        double tmp1, tmp2;
                        if (double.TryParse(args[2], out tmp1)) pu = tmp1;
                        if (double.TryParse(args[3], out tmp2)) su = tmp2;
                    }
                    foreach (int s in new int[] { 16, 32, 48, 64, 256 })
                    {
                        using (var bmp = TrayContext.RenderGauge(s, pu, su, true))
                        {
                            bmp.Save(Path.Combine(args[1], "preview-" + s + ".png"), ImageFormat.Png);
                        }
                    }
                    return;
                }
                if (args[0] == "--icon-variants")
                {
                    using (var bmp = TrayContext.RenderGauge(256, 14, 68, true))
                    {
                        bmp.Save(Path.Combine(args[1], "variant-0.png"), ImageFormat.Png);
                    }
                    return;
                }
                if (args[0] == "--preview-pro")
                {
                    // 用法：--preview-pro <目录>
                    // 生成 Pro 单环示意，并排一张现版双环做对照（浅色 / 深色各一行）。
                    TrayContext.WriteProPreview(args[1]);
                    return;
                }
                if (args[0] == "--test-notify")
                {
                    // 用法：--test-notify <输出文件>；只跑提醒逻辑、写文本，不弹窗
                    TrayContext.WriteNotifySelfTest(args[1]);
                    return;
                }
                if (args[0] == "--test-toast" && args.Length >= 3)
                {
                    // 用法：--test-toast <目录> <wav路径>；出弹窗截图 + 播一次提示音
                    TrayContext.WriteToastPreview(args[1], args[2]);
                    return;
                }
                if (args[0] == "--test-toast-live")
                {
                    // 用法：--test-toast-live <wav路径>；真弹一次通知，让人看进场/退场动画
                    TrayContext.RunToastLive(args.Length >= 2 ? args[1] : null);
                    return;
                }
                if (args[0] == "--find-codex")
                {
                    // 用法：--find-codex <输出文件>；只跑一次查找，把结果写下来
                    TrayContext.WriteCodexProbe(args[1], args.Length >= 3 ? args[2] : null);
                    return;
                }
                if (args[0] == "--preview-locate" && args.Length >= 3)
                {
                    // 用法：--preview-locate <目录> <png>；出识别界面的截图
                    TrayContext.WriteLocatePreview(args[1], args[2]);
                    return;
                }
                if (args[0] == "--demo-toast" && args.Length >= 2)
                {
                    TrayContext.RunToastDemo(args[1], args.Length >= 3 ? args[2] : "a",
                        args.Length >= 4 ? args[3] : null, args.Length >= 5 ? args[4] : null);
                    return;
                }
            }

            if (args != null && args.Length == 2 && args[0] == "--update-window")
            {
                ReleaseUpdates.RunWindow(Application.ExecutablePath, args[1]);
                return;
            }
            if (args != null && args.Length == 4 && args[0] == "--update-silent")
            {
                ReleaseUpdates.RunSilent(Application.ExecutablePath, args[1], args[2], args[3]);
                return;
            }
            if ((args == null || args.Length == 0) && PortableUpgrade.TryRun(Application.ExecutablePath)) return;

            bool createdNew;
            using (var mutex = new Mutex(true, "CodexQuotaTrayAppMutex", out createdNew))
            {
                if (!createdNew)
                {
                    string eventName = SettingsRequestName(args);
                    if (eventName != null)
                    {
                        try { using (var request = EventWaitHandle.OpenExisting(eventName)) request.Set(); }
                        catch (WaitHandleCannotBeOpenedException) { }
                    }
                    return;
                }
                if (args != null && args.Length > 0 && args[0] == "--exit") return;
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Environment.SetEnvironmentVariable("QUOTA_PATH_CHECKER", Application.ExecutablePath);
                using (var context = new TrayContext()) Application.Run(context);
            }
        }
    }

    internal static class PortableUpgrade
    {
        private static readonly string[] StableNames = { "额度显示器.exe", "CodexQuotaTray.exe" };

        internal static bool TryRun(string source)
        {
            Version incoming = Assembly.GetExecutingAssembly().GetName().Version;
            if (!IsReleaseName(Path.GetFileName(source), incoming)) return false;

            using (var guard = new Mutex(false, "CodexQuotaTrayPortableUpgrade"))
            {
                try { if (!guard.WaitOne(0)) return true; }
                catch (AbandonedMutexException) { }

                try
                {
                    using (Process active = FindRunning(null))
                    {
                        string activePath = active == null ? null : active.MainModule.FileName;
                        string target = ResolveTarget(source, activePath, ReadDesktopTargets());
                        if (target == null) return false;
                        if (activePath != null && !SamePath(activePath, target))
                            throw new IOException("Another copy of Codex Quota Tray is running from " + activePath);

                        Version previous = ReadVersion(target);
                        if (previous == null) throw new IOException("The target executable changed during update.");
                        if (incoming <= previous)
                        {
                            StartTarget(target, "--settings");
                            return true;
                        }

                        using (Process running = FindRunning(target))
                            Replace(running, source, target, previous, Program.SettingsRequestName(new[] { "--exit" }));
                        return true;
                    }
                }
                catch (Exception error)
                {
                    string language = L.Guess("auto");
                    string explanation = language == "zh" ? "更新未完成。请确认打开的是要升级的额度显示器。"
                        : language == "ja" ? "更新できませんでした。更新対象のアプリを確認してください。"
                        : "The update could not finish. Check which copy of Codex Quota Tray you want to update.";
                    MessageBox.Show(explanation + "\n\n" + error.Message, "Codex Quota Tray",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return true;
                }
                finally { guard.ReleaseMutex(); }
            }
        }

        internal static bool IsReleaseName(string name, Version version)
        {
            string prefix = "CodexQuotaTray-" + version;
            return Regex.IsMatch(name, "^" + Regex.Escape(prefix) + @"(?: ?\([1-9]\d*\))?\.exe$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        internal static string ResolveTarget(string source, string active, IEnumerable<string> desktopTargets)
        {
            string directory = Path.GetDirectoryName(source);
            if (!IsCandidate(active, source)) active = null;
            if (active != null && SamePath(Path.GetDirectoryName(active), directory)) return active;

            var linked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var linkedHere = new List<string>();
            foreach (string path in desktopTargets)
            {
                if (!IsCandidate(path, source) || !linked.Add(path)) continue;
                if (SamePath(Path.GetDirectoryName(path), directory)) linkedHere.Add(path);
            }
            if (linkedHere.Count > 0) return OnlyCandidate(linkedHere);

            var local = new List<string>();
            foreach (string name in StableNames)
            {
                string path = Path.Combine(directory, name);
                if (IsCandidate(path, source)) local.Add(path);
            }
            if (local.Count > 0) return OnlyCandidate(local);

            if (active != null) linked.Add(active);
            if (linked.Count > 0) return OnlyCandidate(linked);
            if (!File.Exists(Path.Combine(directory, "settings.txt")) &&
                !File.Exists(Path.Combine(directory, "multi-account-status.json"))) return null;

            foreach (string path in Directory.EnumerateFiles(directory, "CodexQuotaTray-*.exe"))
            {
                if (Regex.IsMatch(Path.GetFileName(path), @"^CodexQuotaTray-\d+(?:\.\d+){3}(?: ?\([1-9]\d*\))?\.exe$",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) && IsCandidate(path, source))
                    local.Add(path);
            }
            return OnlyCandidate(local);
        }

        private static string OnlyCandidate(IEnumerable<string> paths)
        {
            string selected = null;
            foreach (string path in paths)
            {
                if (selected != null)
                    throw new IOException("More than one copy was found. Open the copy you want to update, then run the download from its folder.");
                selected = path;
            }
            return selected;
        }

        private static IEnumerable<string> ReadDesktopTargets()
        {
            foreach (var shortcut in TrayContext.ReadDesktopShortcuts(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)))
                yield return shortcut.Value;
        }

        private static bool SamePath(string left, string right) { return String.Equals(left, right, StringComparison.OrdinalIgnoreCase); }

        private static bool IsCandidate(string path, string source)
        {
            return !SamePath(path, source) && ReadVersion(path) != null;
        }

        internal static Version ReadVersion(string path)
        {
            if (!File.Exists(path)) return null;
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) return null;
            FileVersionInfo info = FileVersionInfo.GetVersionInfo(path);
            Version version;
            return info.ProductName == "Codex Quota Tray" && Version.TryParse(info.FileVersion, out version)
                && version.Major >= 1 ? version : null;
        }

        private static Process FindRunning(string target)
        {
            int session, id;
            using (Process current = Process.GetCurrentProcess()) { session = current.SessionId; id = current.Id; }
            Process match = null;
            Process[] processes = target == null ? Process.GetProcesses() : Process.GetProcessesByName(Path.GetFileNameWithoutExtension(target));
            foreach (Process process in processes)
            {
                try
                {
                    if (match != null || process.Id == id || process.SessionId != session) continue;
                    if (target == null)
                    {
                        string name = process.ProcessName;
                        if (!SamePath(name, "额度显示器") && !SamePath(name, "CodexQuotaTray") &&
                            !Regex.IsMatch(name, @"^CodexQuotaTray-[0-9]", RegexOptions.IgnoreCase)) continue;
                    }
                    string path = process.MainModule.FileName;
                    if (target == null ? ReadVersion(path) != null : SamePath(path, target)) match = process;
                }
                catch (System.ComponentModel.Win32Exception) { }
                catch (InvalidOperationException) { }
                catch (IOException) { }
                finally { if (process != match) process.Dispose(); }
            }
            return match;
        }

        private static void StopTarget(Process process, Version previous, string exitSignal)
        {
            if (process == null || process.HasExited) return;
            EventWaitHandle exit;
            if (previous >= new Version(1, 8, 1, 0))
            {
                if (!EventWaitHandle.TryOpenExisting(exitSignal, out exit))
                {
                    if (process.HasExited) return;
                    throw new IOException("The running version is not ready to accept an exit request.");
                }
                using (exit) exit.Set();
                if (!process.WaitForExit(30000))
                    throw new IOException("The running version did not respond to the exit request.");
                return;
            }

            process.CloseMainWindow();
            if (process.WaitForExit(2200)) return;
            process.Kill();
            if (!process.WaitForExit(6000)) throw new IOException("The old process did not exit.");
        }

        private static void StartTarget(string target, string argument)
        {
            using (Process process = Process.Start(new ProcessStartInfo(target, argument) {
                WorkingDirectory = Path.GetDirectoryName(target), UseShellExecute = false }))
            {
                if (process == null || (argument == "--startup" && process.WaitForExit(1200)))
                    throw new IOException("Could not restart the updated executable.");
            }
        }

        internal static void ApplyVerified(string source, string target, Version previous, Action beforeRestart)
        {
            using (var guard = new Mutex(false, "CodexQuotaTrayPortableUpgrade"))
            {
                try
                {
                    if (!guard.WaitOne(0)) throw new IOException("Another update is in progress.");
                }
                catch (AbandonedMutexException) { }
                try
                {
                    using (Process running = FindRunning(target))
                        Replace(running, source, target, previous, Program.SettingsRequestName(new[] { "--exit" }), beforeRestart);
                }
                finally { guard.ReleaseMutex(); }
            }
        }

        internal static void Replace(Process process, string source, string target, Version previous, string exitSignal, Action beforeRestart = null)
        {
            string pending = target + "." + Guid.NewGuid().ToString("N") + ".update";
            string backup = pending + ".backup";
            try
            {
                File.Copy(source, pending);
                StopTarget(process, previous, exitSignal);
                if (ReadVersion(target) != previous)
                    throw new IOException("The target executable changed during update.");
                File.Replace(pending, target, backup);
                if (beforeRestart != null) beforeRestart();
                StartTarget(target, "--startup");
            }
            catch (Exception error)
            {
                try
                {
                    if (File.Exists(backup))
                    {
                        if (File.Exists(target)) File.Replace(backup, target, null);
                        else File.Move(backup, target);
                    }
                    if (process != null && process.HasExited && File.Exists(target))
                        StartTarget(target, "--startup");
                }
                catch (Exception restoreError)
                {
                    throw new IOException(error.Message + "\n" + restoreError.Message +
                        (File.Exists(backup) ? "\nBackup: " + backup : ""), error);
                }
                throw;
            }
            finally
            {
                try { File.Delete(pending); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }

    internal class WindowInfo
    {
        public double used = -1;
        public double windowMins;
        public double resetsAt;
    }

    internal class QuotaResult
    {
        public bool ok;
        public string error;
        public string plan;
        public WindowInfo primary;
        public WindowInfo secondary;
    }

    // 中日英三语。用法：界面文字一律走 L.T / L.F，表里没有的条目原样返回中文，
    // 所以漏翻不会崩、只会显示中文。语言代码：zh / ja / en。
    internal static class L
    {
        public static string Lang = "zh";

        private static readonly System.Collections.Generic.Dictionary<string, string> En =
            new System.Collections.Generic.Dictionary<string, string>();
        private static readonly System.Collections.Generic.Dictionary<string, string> Ja =
            new System.Collections.Generic.Dictionary<string, string>();

        static L()
        {
            string[,] accountTexts = new string[,] {
                { "账号", "Accounts", "アカウント" },
                { "设置", "Settings", "設定" },
                { "尚未绑定账号", "No accounts connected", "未接続" },
                { "未绑定任何账号", "No linked accounts", "連携済みアカウントなし" },
                { "未识别到 Codex", "Codex not found", "Codex が見つかりません" },
                { "连接运行时无法启动。请重新选择 Codex 查询程序，或安装 Node.js 后重试。", "Could not start the connection runtime. Re-select Codex or install Node.js and retry.", "接続用ランタイムを起動できません。Codex を選び直すか、Node.js をインストールして再試行してください。" },
                { "无法启动所选的 Codex 查询程序，请重新选择。", "Could not start the selected Codex executable. Select it again.", "指定された Codex を起動できません。選び直してください。" },
                { "Codex 查询服务启动或握手失败。请检查 Codex 是否能正常运行。", "Could not start or connect to the Codex service. Check that Codex runs normally.", "Codex サービスを開始できないか接続できません。Codex が正常に動作するか確認してください。" },
                { "当前绑定的 Codex 登录不可用，请刷新登录或重新绑定。", "The Codex login for this binding is unavailable. Refresh the login or bind again.", "連携先の Codex のログイン状態を確認できません。ログインを更新するか、連携し直してください。" },
                { "正在读取本地绑定…", "Reading local bindings…", "連携情報を読み込み中…" },
                { "无法读取本地绑定，请手动刷新。", "Could not read local bindings. Refresh to retry.", "連携情報を読み取れません。手動で更新してください。" },
                { "账号连接", "Account connections", "アカウント接続" },
                { "托盘额度显示设置…", "Tray quota display settings…", "トレイ残量の表示設定…" },
                { "托盘额度显示设置", "Tray quota display settings", "トレイ残量の表示設定" },
                { "桌面快捷方式图标", "Desktop shortcut icon", "デスクトップショートカットのアイコン" },
                { "选择桌面额度图标跟随的账号。", "Choose which account the desktop quota icon follows.", "デスクトップの残量アイコンに表示するアカウントを選びます。" },
                { "Codex 查询程序…", "Codex query program…", "照会用の Codex を指定…" },
                { "Codex 查询程序", "Codex query program", "照会用の Codex" },
                { "仅定位查询程序，不会登录或切换账号。", "Only locates the query program. Does not sign in or switch accounts.", "照会プログラムの場所のみ指定します。ログインやアカウント切替は行いません。" },
                { "最多连接五个账号，不切换 Codex 当前登录。", "Connect up to five accounts without switching the current Codex login.", "Codex の現在のログインを切り替えずに最大5つのアカウントを接続できます。" },
                { "当前读取：已连接账号", "Reading: connected accounts", "読み取り元：接続済みアカウント" },
                { "当前读取：Codex 当前登录", "Reading: current Codex login", "読み取り元：Codex の現在のログイン" },
                { "使用各账号的独立登录，不读取 Codex 当前登录。", "Uses each account's separate sign-in, not the current Codex login.", "各アカウントの個別ログインを使用し、Codex の現在のログインは読み取りません。" },
                { "连接账号后自动改读已连接账号。", "After connecting an account, reads the connected accounts instead.", "アカウント接続後は接続済みアカウントの読み取りに切り替わります。" },
                { "账号列表读取失败，请重新打开窗口。", "Cannot read accounts. Close and reopen this window.", "アカウント一覧を取得できません。ウィンドウを開き直してください。" },
                { "账号连接…", "Account connections…", "アカウント接続…" },
                { "账号 {0}", "Account {0}", "アカウント {0}" },
                { "显示设置…", "Display settings…", "表示設定…" },
                { "账号显示设置", "Account display settings", "アカウント表示設定" },
                { "托盘显示账号", "Accounts shown in the tray", "トレイに表示するアカウント" },
                { "桌面来源", "Desktop icon follows", "デスクトップアイコンの参照先" },
                { "托盘至少保留一个账号", "Keep at least one account in the tray.", "トレイに少なくとも1つのアカウントを残してください。" },
                { "账号列表已变化，请重新打开设置。", "The account list changed. Reopen settings.", "アカウント一覧が変わりました。設定を開き直してください。" },
                { "保存设置失败", "Could not save settings.", "設定を保存できませんでした。" },
                { "账号列表读取失败，正在重试。", "Cannot read accounts. Retrying.", "アカウント一覧を取得できません。再試行中です。" },
                { "请先关闭账号连接窗口。", "Close the account connections window first.", "先にアカウント接続ウィンドウを閉じてください。" },
                { "账号连接组件加载失败，正在重试。", "Cannot load account connections. Retrying.", "アカウント接続を読み込めません。再試行中です。" },
                { "所选账号暂未取得最新额度。", "Current quota is not available for the selected account.", "選択したアカウントの最新残量を取得できていません。" },
                { "额度查询失败", "Quota update failed", "残量の更新に失敗しました" },
                { "最后成功", "Last success", "最終成功" },
                { "上次额度", "Last known quota", "前回の残量" },
                { "暂无", "None yet", "まだありません" },
                { "更新失败", "Update failed", "更新失敗" },
                { "保存", "Save", "保存" },
                { "{0}天 剩{1}%", "{0}d {1}% left", "{0}日 残り{1}%" },
                { "5小时 剩{0}%", "5h {0}% left", "5時間 残り{0}%" },
                { "5 小时 剩余 {0}%", "5h {0}% left", "5時間 残り{0}%" },
                { "{0} 天 剩余 {1}%", "{0}d {1}% left", "{0}日 残り{1}%" },
                { "最多连接五个账号，不切换 Codex 当前登录。托盘显示和桌面来源在显示设置中选择。", "Connect up to five accounts without switching the current Codex login. Choose tray accounts and desktop source in Display settings.", "Codex の現在のログインを切り替えずに最大5つのアカウントを接続できます。トレイ表示とデスクトップの参照先は表示設定で選びます。" },
                { "未连接", "Not connected", "未接続" },
                { "连接", "Connect", "接続" },
                { "检查额度", "Check quota", "残量を確認" },
                { "确认此账号", "Confirm account", "このアカウントを確認" },
                { "重新选择", "Choose again", "選び直す" },
                { "取消操作", "Cancel", "操作を中止" },
                { "正在取消…", "Cancelling…", "中止しています…" },
                { "正在检查…", "Checking…", "確認中…" },
                { "待确认", "Awaiting confirmation", "確認待ち" },
                { "已连接", "Connected", "接続済み" },
                { "本次未读到额度", "Quota unavailable for this check", "今回は残量を取得できませんでした" },
                { "连接失败，请重试。", "Connection failed. Try again.", "接続に失敗しました。再試行してください。" },
                { "连接超时，请重试。", "Connection timed out. Try again.", "接続がタイムアウトしました。再試行してください。" },
                { "操作已取消。", "Operation cancelled.", "操作を中止しました。" },
                { "账号已连接，正在检查额度…", "Connected. Checking quota…", "接続しました。残量を確認中…" },
                { "已取消，请重新连接。", "Cancelled. Connect again.", "中止しました。再接続してください。" },
                { "已核对账号并读到额度。", "Account verified and quota received.", "アカウントと残量を確認しました。" },
                { "暂无额度窗口数据", "No quota window data", "残量ウィンドウのデータがありません" },
                { "{0} 小时", "{0} hours", "{0} 時間" },
                { "{0}剩余 {1}%", "{0}: {1}% left", "{0} 残り {1}%" },
                { "账号身份变化，已停止读取。", "Account identity changed. Reading stopped.", "アカウントが変わったため読み取りを停止しました。" },
                { "这个账号已在另一处连接，请选择另一个。", "This account is already connected in the other slot. Choose another.", "このアカウントはもう一方に接続済みです。別のアカウントを選んでください。" },
                { "另一个连接操作正在进行，请稍后重试。", "Another connection operation is running. Try again shortly.", "別の接続操作中です。しばらくして再試行してください。" },
                { "请先连接此账号。", "Connect this account first.", "先にこのアカウントを接続してください。" },
                { "Windows 凭据库不可用，已停止连接。", "Windows credential store unavailable. Connection stopped.", "Windows 資格情報ストアを使えないため接続を停止しました。" },
                { "登录地址不可信，已取消。", "Untrusted sign-in URL. Cancelled.", "信頼できないログイン先のため中止しました。" },
                { "无法打开官方登录页，请重试。", "Cannot open the official sign-in page. Try again.", "公式ログインページを開けません。再試行してください。" },
                { "无法打开账号连接，请重试。", "Cannot open account connections. Try again.", "アカウント接続を開けません。再試行してください。" },
                { "分别登录两个账号，不切换 Codex 当前登录。图标与原详情暂不改变。", "Sign in to each account separately. The current Codex login, icon and quota details stay unchanged.", "各アカウントに個別にログインします。Codex の現在のログイン、アイコン、残量の詳細は変更しません。" },
                { "请在官方网页完成登录，然后回到这里核对账号。", "Sign in on the official page, then return here to verify the account.", "公式ページでログインし、ここでアカウントを確認してください。" },
                { "请核对账号；正确就点“确认此账号”，否则点“重新选择”。", "Check the account, then confirm it or choose again.", "アカウントを確認し、正しければ確定、違えば選び直してください。" },
                { "授权保存在本机 Windows 凭据库，不随程序复制。", "Authorization stays in this PC's Windows credential store; copying the app does not copy it.", "認証情報はこの PC の Windows 資格情報ストアに保存され、アプリと一緒にはコピーされません。" }
            };
            for (int i = 0; i < accountTexts.GetLength(0); i++)
            {
                En[accountTexts[i, 0]] = accountTexts[i, 1];
                Ja[accountTexts[i, 0]] = accountTexts[i, 2];
            }
            // ---------- 菜单与窗口 ----------
            En["额度显示器"] = "Quota Tray";
            En["有新版本"] = "Update available";
            En["最新版本 {0}"] = "Latest version {0}";
            En["正在更新"] = "Updating";
            En["正在检查更新…"] = "Checking for updates…";
            En["未找到可以应用的更新。"] = "No update is available to install.";
            En["正在下载 {0}…"] = "Downloading {0}…";
            En["正在替换程序文件…"] = "Replacing application files…";
            En["更新已完成"] = "Update completed";
            En["更新失败"] = "Update failed";
            En["已更新到 {0}"] = "Updated to {0}";
            En["正在启动额度显示器…"] = "Starting Quota Tray…";
            En["刷新"] = "Refresh";
            En["额度详情"] = "Quota details";
            En["额度提醒"] = "Quota alerts";
            En["关闭提醒"] = "Off";
            En["低档"] = "Low";
            En["中档"] = "Medium";
            En["高档"] = "High";
            En["开机自启动"] = "Start with Windows";
            En["开启开机自启动"] = "Enable autostart";
            En["关闭开机自启动"] = "Disable autostart";
            En["创建桌面快捷方式"] = "Create desktop shortcut";
            En["退出"] = "Exit";
            En["语言"] = "Language";
            En["自动"] = "Auto";

            Ja["额度显示器"] = "クォータトレイ";
            Ja["有新版本"] = "新しいバージョンがあります";
            Ja["最新版本 {0}"] = "最新バージョン {0}";
            Ja["正在更新"] = "更新中";
            Ja["正在检查更新…"] = "更新を確認中…";
            Ja["未找到可以应用的更新。"] = "適用できる更新がありません。";
            Ja["正在下载 {0}…"] = "{0} をダウンロード中…";
            Ja["正在替换程序文件…"] = "プログラムファイルを置き換え中…";
            Ja["更新已完成"] = "更新が完了しました";
            Ja["更新失败"] = "更新できませんでした";
            Ja["已更新到 {0}"] = "{0} に更新しました";
            Ja["正在启动额度显示器…"] = "クォータトレイを起動中…";
            Ja["刷新"] = "更新";
            Ja["额度详情"] = "残量の詳細";
            Ja["额度提醒"] = "残量アラート";
            Ja["关闭提醒"] = "オフ";
            Ja["低档"] = "低";
            Ja["中档"] = "中";
            Ja["高档"] = "高";
            Ja["开机自启动"] = "起動時に開始";
            Ja["开启开机自启动"] = "自動起動を有効化";
            Ja["关闭开机自启动"] = "自動起動を無効化";
            Ja["创建桌面快捷方式"] = "デスクトップにショートカットを作成";
            Ja["退出"] = "終了";
            Ja["语言"] = "言語";
            Ja["自动"] = "自動";

            // ---------- 档位悬停说明（触发时额度还剩多少） ----------
            En["不弹任何提醒"] = "No alerts";
            En["剩 25% 和 10% 时各提醒一次"] = "Alerts once at 25% left and once at 10% left";
            En["剩 75%、25%、10% 时各提醒一次"] = "Alerts at 75%, 25% and 10% left";
            En["剩 75%、50%、25%、10% 时各提醒一次"] = "Alerts at 75%, 50%, 25% and 10% left";
            En["7 天额度固定剩 25%、10%"] = "The 7-day quota always alerts at 25% and 10% left";

            Ja["不弹任何提醒"] = "通知しません";
            Ja["剩 25% 和 10% 时各提醒一次"] = "残り25%と10%の時に一度ずつ通知します";
            Ja["剩 75%、25%、10% 时各提醒一次"] = "残り75%・25%・10%の時に通知します";
            Ja["剩 75%、50%、25%、10% 时各提醒一次"] = "残り75%・50%・25%・10%の時に通知します";
            Ja["7 天额度固定剩 25%、10%"] = "7日間の残量は常に残り25%・10%で通知します";

            // ---------- 托盘提示 ----------
            En["额度显示器｜正在读取额度…"] = "Quota Tray | Reading quota…";
            En["正在读取额度…"] = "Reading quota…";
            En["（右键可退出）"] = "(right-click to exit)";
            En["5 小时"] = "5-hour";
            En["{0} 天"] = "{0}-day";
            En["5 小时 剩余 {0}%｜{1}"] = "5-hour: {0}% left · {1}";
            En["{0} 天 剩余 {1}%｜{2}"] = "{0}-day: {1}% left · {2}";
            En["5 小时 不限制"] = "5-hour: unlimited";
            En["{0} 天 暂无数据"] = "{0}-day: no data";

            Ja["额度显示器｜正在读取额度…"] = "クォータトレイ｜残量を読み込み中…";
            Ja["正在读取额度…"] = "残量を読み込み中…";
            Ja["（右键可退出）"] = "(右クリックで終了)";
            Ja["5 小时"] = "5時間";
            Ja["{0} 天"] = "{0}日";
            Ja["5 小时 剩余 {0}%｜{1}"] = "5時間: 残り{0}%・{1}";
            Ja["{0} 天 剩余 {1}%｜{2}"] = "{0}日: 残り{1}%・{2}";
            Ja["5 小时 不限制"] = "5時間: 制限なし";
            Ja["{0} 天 暂无数据"] = "{0}日: データなし";

            // ---------- 倒计时 ----------
            En["恢复时间未知"] = "reset time unknown";
            En["即将恢复"] = "resetting now";
            En["{0} 天 {1} 小时后恢复"] = "resets in {0}d {1}h";
            En["{0} 小时 {1} 分后恢复"] = "resets in {0}h {1}m";
            En["{0} 分钟后恢复"] = "resets in {0}m";

            Ja["恢复时间未知"] = "リセット時刻は不明";
            Ja["即将恢复"] = "まもなくリセット";
            Ja["{0} 天 {1} 小时后恢复"] = "あと{0}日{1}時間でリセット";
            Ja["{0} 小时 {1} 分后恢复"] = "あと{0}時間{1}分でリセット";
            Ja["{0} 分钟后恢复"] = "あと{0}分でリセット";

            // ---------- 弹窗正文 ----------
            En["{0}额度还剩 {1}%｜{2}"] = "{0} quota: {1}% left · {2}";
            En["{0}额度只剩 {1}%｜{2}"] = "{0} quota: only {1}% left · {2}";
            En["{0}额度已耗尽"] = "{0} quota used up";
            En["{0}额度已恢复"] = "{0} quota restored";
            En["今日推荐用量已到"] = "Daily pace reached";
            En["已达今日推荐用量"] = "Daily pace reached";

            Ja["{0}额度还剩 {1}%｜{2}"] = "{0}の残量: {1}%・{2}";
            Ja["{0}额度只剩 {1}%｜{2}"] = "{0}の残量: 残りわずか{1}%・{2}";
            Ja["{0}额度已耗尽"] = "{0}の残量を使い切りました";
            Ja["{0}额度已恢复"] = "{0}の残量が回復しました";
            Ja["今日推荐用量已到"] = "本日の推奨使用量に到達";
            Ja["已达今日推荐用量"] = "本日の推奨使用量に到達";

            // ---------- 详情窗 ----------
            En["Codex 额度"] = "Codex quota";
            En["套餐：{0}"] = "Plan: {0}";
            En["5 小时额度"] = "5-hour quota";
            En["{0} 天额度"] = "{0}-day quota";
            En["剩余"] = "Remaining";
            En["暂无"] = "No data";
            En["剩余 {0}%"] = "{0}% left";
            En["已耗尽"] = "used up";
            En["恢复：{0}"] = "Resets: {0}";
            En["暂无数据"] = "No data";
            En["不限制（当前套餐不含这项限额）"] = "Unlimited in this plan";
            En["关闭"] = "Close";
            En["正在读取额度，请稍等一下。"] = "Reading quota, one moment.";
            En["还没拿到额度数据。\r\n\r\n原因：{0}"] = "No quota data yet.\r\n\r\nReason: {0}";
            En["未知"] = "unknown";
            En["{0}（已到期）"] = "{0} (expired)";
            En["{0}（还有 {1}）"] = "{0} (in {1})";
            En["{0} 天 {1} 小时"] = "{0}d {1}h";
            En["{0} 小时 {1} 分"] = "{0}h {1}m";
            En["{0} 分钟"] = "{0}m";

            Ja["Codex 额度"] = "Codex クォータ";
            Ja["套餐：{0}"] = "プラン: {0}";
            Ja["5 小时额度"] = "5時間の残量";
            Ja["{0} 天额度"] = "{0}日の残量";
            Ja["剩余"] = "残量";
            Ja["暂无"] = "データなし";
            Ja["剩余 {0}%"] = "残り{0}%";
            Ja["已耗尽"] = "使い切り";
            Ja["恢复：{0}"] = "リセット: {0}";
            Ja["暂无数据"] = "データなし";
            Ja["不限制（当前套餐不含这项限额）"] = "このプランでは制限なし";
            Ja["关闭"] = "閉じる";
            Ja["正在读取额度，请稍等一下。"] = "残量を読み込み中です。少々お待ちください。";
            Ja["还没拿到额度数据。\r\n\r\n原因：{0}"] = "残量データを取得できませんでした。\r\n\r\n理由: {0}";
            Ja["未知"] = "不明";
            Ja["{0}（已到期）"] = "{0} (期限切れ)";
            Ja["{0}（还有 {1}）"] = "{0} (あと {1})";
            Ja["{0} 天 {1} 小时"] = "{0}日{1}時間";
            Ja["{0} 小时 {1} 分"] = "{0}時間{1}分";
            Ja["{0} 分钟"] = "{0}分";

            // ---------- 气泡提示 ----------
            En["设置失败"] = "Settings failed";
            En["写注册表没成功，可能是权限问题。"] = "Could not write the registry. This may be a permissions problem.";
            En["已开启开机自启动"] = "Autostart enabled";
            En["已关闭开机自启动"] = "Autostart disabled";
            En["下次登录 Windows 就会自动启动。"] = "It will start the next time you sign in.";
            En["下次登录不会再自动启动。"] = "It will not start automatically next time.";
            En["已创建"] = "Created";
            En["桌面快捷方式已就位，图标会跟着额度变化。"] = "Desktop shortcut is ready. Its icon follows your quota.";
            En["额度显示器已就绪"] = "Quota Tray is ready";
            En["桌面图标会跟着额度一起变。右键托盘图标可查看详情。"] = "The desktop icon follows your quota. Right-click the tray icon for details.";
            En["已关闭，不再弹通知。"] = "Alerts turned off.";
            En["已设为{0}。"] = "Set to {0}.";

            Ja["设置失败"] = "設定に失敗しました";
            Ja["写注册表没成功，可能是权限问题。"] = "レジストリに書き込めませんでした。権限の問題かもしれません。";
            Ja["已开启开机自启动"] = "自動起動を有効にしました";
            Ja["已关闭开机自启动"] = "自動起動を無効にしました";
            Ja["下次登录 Windows 就会自动启动。"] = "次回サインイン時に自動で起動します。";
            Ja["下次登录不会再自动启动。"] = "次回からは自動起動しません。";
            Ja["已创建"] = "作成しました";
            Ja["桌面快捷方式已就位，图标会跟着额度变化。"] = "デスクトップにショートカットを作成しました。アイコンは残量に合わせて変わります。";
            Ja["额度显示器已就绪"] = "クォータトレイの準備ができました";
            Ja["桌面图标会跟着额度一起变。右键托盘图标可查看详情。"] = "デスクトップのアイコンも残量に合わせて変わります。トレイアイコンを右クリックすると詳細を表示できます。";
            Ja["已关闭，不再弹通知。"] = "アラートをオフにしました。";
            Ja["已设为{0}。"] = "{0} に設定しました。";

            // ---------- 错误 ----------
            En["找不到 quota.js"] = "quota.js not found";
            En["查询超时"] = "Query timed out";
            En["查询无输出"] = "Query returned nothing";
            En["返回格式异常"] = "Unexpected response format";
            En["查询失败，稍后重试"] = "Query failed, will retry";

            Ja["找不到 quota.js"] = "quota.js が見つかりません";
            Ja["查询超时"] = "タイムアウトしました";
            Ja["查询无输出"] = "応答がありません";
            Ja["返回格式异常"] = "応答の形式が想定外です";
            Ja["查询失败，稍后重试"] = "取得に失敗しました。後で再試行します";

            // ---------- 桌面快捷方式 ----------
            En["额度显示器.lnk"] = "Codex Quota.lnk";
            En["Codex 额度显示器，右下角托盘"] = "Codex quota in the notification area";

            Ja["额度显示器.lnk"] = "クォータトレイ.lnk";
            Ja["Codex 额度显示器，右下角托盘"] = "Codex のクォータを通知領域に表示";

            // ---------- 测试台弹窗 ----------
            En["5 小时额度已耗尽"] = "5-hour quota used up";
            En["7 天额度已耗尽"] = "7-day quota used up";
            En["5 小时额度只剩 10%｜30 分后恢复"] = "5-hour quota: only 10% left · resets in 30m";
            En["5 小时额度还剩 75%｜2 小时后恢复"] = "5-hour quota: 75% left · resets in 2h";
            En["5 小时额度只剩 10%｜1 小时 51 分后恢复"] = "5-hour quota: only 10% left · resets in 1h 51m";
            En["5 小时额度还剩 19%｜2 小时 22 分后恢复"] = "5-hour quota: 19% left · resets in 2h 22m";

            Ja["5 小时额度已耗尽"] = "5時間の残量を使い切りました";
            Ja["7 天额度已耗尽"] = "7日の残量を使い切りました";
            Ja["5 小时额度只剩 10%｜30 分后恢复"] = "5時間の残量: 残りわずか10%・あと30分でリセット";
            Ja["5 小时额度还剩 75%｜2 小时后恢复"] = "5時間の残量: 75%・あと2時間でリセット";
            Ja["5 小时额度只剩 10%｜1 小时 51 分后恢复"] = "5時間の残量: 残りわずか10%・あと1時間51分でリセット";
            Ja["5 小时额度还剩 19%｜2 小时 22 分后恢复"] = "5時間の残量: 19%・あと2時間22分でリセット";
            En["找不到 Codex"] = "Codex not found";
            En["请先打开 Codex 桌面版并登录，然后点「自动搜寻」；找不到就点「浏览」手动指定 codex.exe。"] = "Open the Codex desktop app and sign in, then click Auto-detect. If that fails, click Browse and pick codex.exe yourself.";
            En["浏览…"] = "Browse…";
            En["自动搜寻"] = "Auto-detect";
            En["正在搜寻…"] = "Searching…";
            En["没找到，请点「浏览」手动指定"] = "Not found. Use Browse to pick it manually.";
            En["已找到"] = "Found";
            En["已选择"] = "Selected";
            En["选择 codex.exe"] = "Select codex.exe";
            En["所有程序"] = "All programs";
            En["确定"] = "OK";
            En["取消"] = "Cancel";
            En["这个路径不存在，请重新选择 codex.exe。"] = "That path doesn't exist. Pick codex.exe again.";
            En["识别 Codex…"] = "Locate Codex…";
            Ja["找不到 Codex"] = "Codex が見つかりません";
            Ja["请先打开 Codex 桌面版并登录，然后点「自动搜寻」；找不到就点「浏览」手动指定 codex.exe。"] = "Codex デスクトップ版を起動してサインインし、「自動検索」を押してください。見つからない場合は「参照」で codex.exe を指定してください。";
            Ja["浏览…"] = "参照…";
            Ja["自动搜寻"] = "自動検索";
            Ja["正在搜寻…"] = "検索中…";
            Ja["没找到，请点「浏览」手动指定"] = "見つかりません。「参照」で指定してください。";
            Ja["已找到"] = "見つかりました";
            Ja["已选择"] = "選択済み";
            Ja["选择 codex.exe"] = "codex.exe を選択";
            Ja["所有程序"] = "すべてのプログラム";
            Ja["确定"] = "OK";
            Ja["取消"] = "キャンセル";
            Ja["这个路径不存在，请重新选择 codex.exe。"] = "そのパスは存在しません。codex.exe を選び直してください。";
            En["找不到 codex.exe（Codex 桌面端没装？）"] = "codex.exe not found. Is the Codex desktop app installed?";
            En["查询超时（Codex 没响应或网络不通）"] = "Query timed out. Codex is not responding, or the network is down.";
            En["接口没返回额度数据"] = "Codex returned no quota data";
            En["启动 codex 失败"] = "Could not start Codex";
            Ja["找不到 codex.exe（Codex 桌面端没装？）"] = "codex.exe が見つかりません。Codex デスクトップ版は入っていますか？";
            Ja["查询超时（Codex 没响应或网络不通）"] = "タイムアウトしました。Codex が応答しないか、ネットワークに繋がっていません。";
            Ja["接口没返回额度数据"] = "Codex がクォータ情報を返しませんでした";
            Ja["启动 codex 失败"] = "Codex を起動できませんでした";
            En["点「自动搜寻」；不行就选 Codex 的安装文件夹，或直接选 codex.exe。"] = "Click Auto-detect. If that fails, pick the Codex install folder or the exe itself.";
            En["选文件夹…"] = "Folder…";
            En["选程序…"] = "Program…";
            En["选择 Codex 的安装文件夹"] = "Select the Codex install folder";
            En["这个文件夹里没找到 codex.exe"] = "No codex.exe in that folder";
            En["没找到，选个文件夹或直接选程序"] = "Not found. Pick a folder or the program.";
            Ja["点「自动搜寻」；不行就选 Codex 的安装文件夹，或直接选 codex.exe。"] = "「自動検索」を押してください。だめなら Codex のインストールフォルダか exe を選んでください。";
            Ja["选文件夹…"] = "フォルダ…";
            Ja["选程序…"] = "プログラム…";
            Ja["选择 Codex 的安装文件夹"] = "Codex のインストールフォルダを選択";
            Ja["这个文件夹里没找到 codex.exe"] = "そのフォルダに codex.exe はありません";
            En["选择 Codex 的安装文件夹或 Codex.exe 以连接程序。"] = "Pick the Codex install folder or Codex.exe to connect.";
            En["选择…"] = "Choose…";
            En["选择 Codex 的安装文件夹或 Codex.exe"] = "Pick the Codex install folder or Codex.exe";
            En["选择此文件夹"] = "Select this folder";
            En["所有文件"] = "All files";
            En["这里没找到 codex.exe"] = "No codex.exe here";
            En["这里没找到 codex.exe，换个位置试试。"] = "No codex.exe here. Try another location.";
            Ja["选择 Codex 的安装文件夹或 Codex.exe 以连接程序。"] = "Codex のインストールフォルダか Codex.exe を選んで接続します。";
            Ja["选择…"] = "選択…";
            Ja["选择 Codex 的安装文件夹或 Codex.exe"] = "Codex のフォルダまたは Codex.exe を選択";
            Ja["选择此文件夹"] = "このフォルダを選択";
            Ja["所有文件"] = "すべてのファイル";
            Ja["这里没找到 codex.exe"] = "ここに codex.exe はありません";
            Ja["这里没找到 codex.exe，换个位置试试。"] = "ここに codex.exe はありません。別の場所を試してください。";

            Ja["没找到，选个文件夹或直接选程序"] = "見つかりません。フォルダかプログラムを選んでください。";


            Ja["识别 Codex…"] = "Codex を指定…";

        }

        private static string Lookup(System.Collections.Generic.Dictionary<string, string> d, string zh)
        {
            string v;
            return d.TryGetValue(zh, out v) ? v : zh;
        }

        public static string T(string zh)
        {
            if (zh == null || Lang == "zh") return zh;
            return Lang == "ja" ? Lookup(Ja, zh) : Lookup(En, zh);
        }

        public static string F(string zh, params object[] args)
        {
            return string.Format(T(zh), args);
        }

        // 没有 TrayContext 的场合（测试台、预览）用它定语言：先看 settings.txt，再看系统语言
        public static void AutoDetect()
        {
            Lang = Guess("auto");
            try
            {
                string dir = Path.GetDirectoryName(Application.ExecutablePath);
                AppSettings s = AppSettings.Load(dir);
                Lang = Guess(s.language);
            }
            catch { }
        }

        // 自动 = 跟随系统；其余按设置来
        public static string Guess(string setting)
        {
            if (setting == "zh" || setting == "ja" || setting == "en") return setting;
            string c = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
            if (c == "zh") return "zh";
            if (c == "ja") return "ja";
            return "en";
        }
    }

    // 提醒档位：-1 关，0 低，1 中，2 高。默认低档（剩 25% 和剩 10% 各一次）。
    internal class AppSettings
    {
        public int notifyLevel = 0;
        public bool notifySound = true;
        internal const int DefaultNotifyVolume = 100;
        public int notifyVolume = DefaultNotifyVolume;
        public bool dailyPaceDefault = true;
        public bool autoUpdate = true;
        public string theme = "auto";
        public string language = "auto";   // auto / zh / ja / en
        public string codexPath = "";      // 手动指定或自动找到的 codex.exe
        public bool codexLookupAttempted;
        public bool bindingSetupDone;
        internal bool HasCodex { get { return !String.IsNullOrEmpty(codexPath) && File.Exists(codexPath); } }

        internal string DetectCodexOnce(string root, Func<string> find)
        {
            // A saved MSIX path may disappear when the Store updates Codex.
            if (HasCodex || (codexLookupAttempted && String.IsNullOrEmpty(codexPath))) return null;
            codexLookupAttempted = true;
            codexPath = find() ?? "";
            Save(root);
            return codexPath;
        }
        private readonly System.Collections.Generic.Dictionary<string, int> accountLevels = new System.Collections.Generic.Dictionary<string, int>();
        private readonly System.Collections.Generic.Dictionary<string, bool> accountDailyPace = new System.Collections.Generic.Dictionary<string, bool>();
        internal bool HasAccountLevel(string slot) { return accountLevels.ContainsKey(slot); }
        internal bool HasAccountDailyPace(string slot) { return slot != null && accountDailyPace.ContainsKey(slot); }
        internal bool DailyPaceFor(string slot)
        {
            bool value;
            return slot != null && accountDailyPace.TryGetValue(slot, out value) ? value : dailyPaceDefault;
        }
        internal bool SetAccountDailyPace(string root, string slot, bool enabled)
        {
            if (!DisplayPolicy.ValidSlot(slot)) return false;
            bool hadPrevious = accountDailyPace.ContainsKey(slot);
            bool previous = hadPrevious && accountDailyPace[slot];
            accountDailyPace[slot] = enabled;
            if (Save(root)) return true;
            if (hadPrevious) accountDailyPace[slot] = previous;
            else accountDailyPace.Remove(slot);
            return false;
        }
        internal bool ApplyGlobalDailyPace(string root, bool enabled)
        {
            bool previousDefault = dailyPaceDefault;
            var previousOverrides = new System.Collections.Generic.Dictionary<string, bool>(accountDailyPace);
            dailyPaceDefault = enabled;
            accountDailyPace.Clear();
            if (Save(root)) return true;
            dailyPaceDefault = previousDefault;
            foreach (var previous in previousOverrides) accountDailyPace.Add(previous.Key, previous.Value);
            return false;
        }
        internal int LevelFor(string slot)
        {
            int value;
            return slot != null && accountLevels.TryGetValue(slot, out value) ? value : notifyLevel;
        }
        internal void SetAccountLevel(string slot, int level)
        {
            if (!DisplayPolicy.ValidSlot(slot) || level < -1 || level > 2) throw new ArgumentException("Invalid reminder preference");
            accountLevels[slot] = level;
        }
        internal bool ApplyGlobalLevel(string root, int level)
        {
            if (level < -1 || level > 2) return false;
            int previousDefault = notifyLevel;
            var previousLevels = new System.Collections.Generic.Dictionary<string, int>(accountLevels);
            notifyLevel = level;
            accountLevels.Clear();
            if (Save(root)) return true;
            notifyLevel = previousDefault;
            foreach (var previous in previousLevels) accountLevels.Add(previous.Key, previous.Value);
            return false;
        }

        internal bool SetNotifySound(string root, bool enabled)
        {
            bool previous = notifySound;
            notifySound = enabled;
            if (Save(root)) return true;
            notifySound = previous;
            return false;
        }

        internal bool SetNotifyVolume(string root, int volume)
        {
            if (volume < 0 || volume > 100) return false;
            int previous = notifyVolume;
            notifyVolume = volume;
            if (Save(root)) return true;
            notifyVolume = previous;
            return false;
        }

        internal bool SetAutoUpdate(string root, bool enabled)
        {
            bool previous = autoUpdate;
            autoUpdate = enabled;
            if (Save(root)) return true;
            autoUpdate = previous;
            return false;
        }

        private static int ParseLevel(string value)
        {
            switch (value) { case "off": case "none": case "-1": return -1; case "medium": case "mid": case "1": return 1; case "high": case "2": return 2; default: return 0; }
        }
        private static string LevelKey(int value) { return value < 0 ? "off" : value == 1 ? "medium" : value >= 2 ? "high" : "low"; }

        public static AppSettings Load(string root)
        {
            AppSettings s = new AppSettings();
            try
            {
                string p = Path.Combine(root, "settings.txt");
                if (!File.Exists(p)) return s;
                foreach (string raw in File.ReadAllLines(p, Encoding.UTF8))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#")) continue;
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string k = line.Substring(0, eq).Trim().ToLowerInvariant();
                    string v = line.Substring(eq + 1).Trim().ToLowerInvariant();
                    if (k == "codexpath")
                    {
                        s.codexPath = line.Substring(eq + 1).Trim();
                    }
                    else if (k == "theme") s.theme = UiTheme.Normalize(v);
                    else if (k == "bindingsetupdone") s.bindingSetupDone = v == "true";
                    else if (k == "codexlookupattempted") s.codexLookupAttempted = v == "true";
                    else if (k == "notifysound" && (v == "true" || v == "false")) s.notifySound = v == "true";
                    else if (k == "dailypace" && (v == "true" || v == "false")) s.dailyPaceDefault = v == "true";
                    else if (k == "autoupdate" && (v == "true" || v == "false")) s.autoUpdate = v == "true";
                    else if (k == "notifyvolume")
                    {
                        int volume;
                        if (Int32.TryParse(v, out volume) && volume >= 0 && volume <= 100) s.notifyVolume = volume;
                    }
                    else if (k.StartsWith("dailypace.") && DisplayPolicy.ValidSlot(k.Substring(10)) && (v == "true" || v == "false"))
                        s.accountDailyPace[k.Substring(10)] = v == "true";
                    else if (k.StartsWith("notify.") && DisplayPolicy.ValidSlot(k.Substring(7))) s.accountLevels[k.Substring(7)] = ParseLevel(v);
                    else if (k == "language")
                    {
                        if (v == "zh" || v == "ja" || v == "en" || v == "auto") s.language = v;
                    }
                    else if (k == "notifylevel")
                    {
                        if (v == "off" || v == "none" || v == "-1") s.notifyLevel = -1;
                        else if (v == "low" || v == "0") s.notifyLevel = 0;
                        else if (v == "medium" || v == "mid" || v == "1") s.notifyLevel = 1;
                        else if (v == "high" || v == "2") s.notifyLevel = 2;
                    }
                }
            }
            catch { }
            return s;
        }

        public string Key()
        {
            if (notifyLevel < 0) return "off";
            if (notifyLevel >= 2) return "high";
            if (notifyLevel == 1) return "medium";
            return "low";
        }

        public string Display()
        {
            if (notifyLevel < 0) return L.T("关闭提醒");
            if (notifyLevel >= 2) return L.T("高档");
            if (notifyLevel == 1) return L.T("中档");
            return L.T("低档");
        }

        public bool Save(string root)
        {
            string temp = null;
            try
            {
                string text = "# 额度提醒档位：off / low / medium / high" + Environment.NewLine
                            + "notifyLevel=" + Key() + Environment.NewLine
                            + "notifySound=" + (notifySound ? "true" : "false") + Environment.NewLine
                            + "notifyVolume=" + notifyVolume + Environment.NewLine
                            + "dailyPace=" + (dailyPaceDefault ? "true" : "false") + Environment.NewLine
                            + "autoUpdate=" + (autoUpdate ? "true" : "false") + Environment.NewLine
                            + "# 语言：auto / zh / ja / en" + Environment.NewLine
                            + "language=" + language + Environment.NewLine
                            + "# 找不到 Codex 时可手填 codex.exe 完整路径" + Environment.NewLine
                            + (string.IsNullOrEmpty(codexPath) ? "" : "codexPath=" + codexPath + Environment.NewLine);
                text += "theme=" + UiTheme.Normalize(theme) + Environment.NewLine;
                text += "bindingSetupDone=" + bindingSetupDone.ToString().ToLowerInvariant() + Environment.NewLine;
                text += "codexLookupAttempted=" + codexLookupAttempted.ToString().ToLowerInvariant() + Environment.NewLine;
                foreach (string slot in new string[] { "a", "b", "c", "d", "e" })
                    if (accountLevels.ContainsKey(slot)) text += "notify." + slot + "=" + LevelKey(accountLevels[slot]) + Environment.NewLine;
                foreach (string slot in new string[] { "a", "b", "c", "d", "e" })
                    if (accountDailyPace.ContainsKey(slot)) text += "dailyPace." + slot + "=" + (accountDailyPace[slot] ? "true" : "false") + Environment.NewLine;
                string target = Path.Combine(root, "settings.txt");
                temp = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
                File.WriteAllText(temp, text, new UTF8Encoding(false));
                if (File.Exists(target)) File.Replace(temp, target, null);
                else File.Move(temp, target);
                return true;
            }
            catch { return false; }
            finally
            {
                if (temp != null && File.Exists(temp)) try { File.Delete(temp); } catch { }
            }
        }
    }

    // 触发阈值。数组里是"已用百分比"，但用户是按"还剩多少"来描述的，两者必须换算清楚：
    //   低档  剩 25% / 剩 10%              → 已用 75 / 90
    //   中档  剩 75% / 剩 25% / 剩 10%     → 已用 25 / 75 / 90
    //   高档  剩 75% / 50% / 25% / 10%     → 已用 25 / 50 / 75 / 90
    // 别再照着"用掉 25%"去写低档——2026-11-13 就是这么错的，低档在剩 75% 就提前弹了。
    internal static class NotifyRules
    {
        public static int[] Tier(int level)
        {
            if (level >= 2) return new int[] { 25, 50, 75, 90 };
            if (level == 1) return new int[] { 25, 75, 90 };
            return new int[] { 75, 90 };
        }

        // 7天独立规则；5小时的Tier保持原样。100%耗尽由NotifyWindow单独处理。
        public static int[] LongTier(int level)
        {
            if (level < 0) return new int[0];
            if (level >= 2) return new int[] { 10, 20, 30, 40, 50, 60, 70, 80, 90 };
            if (level == 1) return new int[] { 25, 50, 75, 90 };
            return new int[] { 85, 95 };
        }
        public static int[] LongFixed() { return LongTier(0); }

        internal static bool CanShowRecovery(string plan, int days)
        {
            return !PlanNames.Normalize(plan).StartsWith("pro_", StringComparison.Ordinal) || days == 7;
        }

        internal static string RecoveryBody(int days)
        {
            return L.F("{0}额度已恢复", days == 0 ? L.T("5 小时") : L.F("{0} 天", days));
        }

        internal static string DailyPaceBody()
        {
            return L.T("已达今日推荐用量");
        }
    }

    // 一个额度窗口的提醒状态。
    // 关键：查询是跳着走的（7 天额度 5 分钟才一次），所以用"跨过"判断而不是"等于"；
    // 额度重置后重新上膛；程序刚启动时先静默记基线，不立刻弹。
    internal class NotifyWindow
    {
        public string name = "";
        public int[] thresholds = new int[0];

        private const double MinUsedForRecovery = 1.0;
        private double lastUsed = -1;
        private double lastReset = -1;
        private bool emptyFired;
        private bool usedInWindow;
        private readonly System.Collections.Generic.Dictionary<int, bool> fired =
            new System.Collections.Generic.Dictionary<int, bool>();

        // 本次采样是否刚跨到"用光"。由调用方决定发不发耗尽提示。
        public bool Exhausted;
        public bool Recovered { get; private set; }

        // 返回本次触发的阈值；0 表示没触发
        public int Fire(double used, double resetsAt)
        {
            Exhausted = false;
            Recovered = false;
            if (used < 0)
            {
                lastUsed = -1;
                return 0;
            }

            bool hasResetTimes = lastReset > 0 && resetsAt > 0;
            bool largeDrop = lastUsed >= 0 && used + 30 < lastUsed;
            bool reset = (hasResetTimes && Math.Abs(resetsAt - lastReset) > 1) || largeDrop;
            if (reset)
            {
                Recovered = usedInWindow && lastUsed > used && (hasResetTimes ? resetsAt > lastReset + 1 : largeDrop);
                fired.Clear();
                emptyFired = false;
                usedInWindow = used >= MinUsedForRecovery;
                lastUsed = used;
                lastReset = resetsAt;
                return 0;
            }

            if (used >= MinUsedForRecovery) usedInWindow = true;
            bool baseline = lastUsed < 0;
            double prev = lastUsed;
            lastUsed = used;
            lastReset = resetsAt;
            if (baseline) return 0;

            // 用光了：单独一档，不走阈值表，因为文案和提示音都不一样
            if (prev < 100.0 && used >= 100.0 && !emptyFired)
            {
                emptyFired = true;
                Exhausted = true;
                return 0;
            }

            int best = 0;
            foreach (int t in thresholds)
            {
                if (prev < t && used >= t && !fired.ContainsKey(t) && t > best) best = t;
            }
            if (best > 0) fired[best] = true;
            return best;
        }
    }

    // 自绘提醒小窗：跟随设置窗采用系统 DWM 亚克力材质与圆角（老系统自动回退为实色圆角），
    // 不走系统通知中心，也不抢焦点。
    internal class ToastForm : Form
    {
        private const int CardW = 376;
        private const int TextInset = 27;
        private const int CornerRadius = UiTheme.WindowRadius;

        private readonly float dpiScale;
        private readonly string titleText;
        private readonly string bodyText;
        private readonly string resetText;
        private readonly string emailMask;
        private readonly bool compact;
        private readonly Color accountColor;
        private bool layered = true;
        private readonly UiLiveBackdrop backdrop;
        internal bool GlassEnabled { get { return layered && backdrop.Frame != null; } }
        private readonly EventHandler themeChanged;
        private readonly Font smallFont;
        private readonly Font titleFont;
        private readonly Font bodyFont;
        private readonly System.Windows.Forms.Timer life;

        // 进场/退场动画：从屏幕右边缘滑进来，停 7 秒，再原路滑回去
        private System.Windows.Forms.Timer anim;
        private int animElapsed;
        private int animDuration;
        private bool animIn;
        private bool dismissing;
        private int endX;
        private int yPos;
        private const int SlideInMs = 260;
        private const int SlideOutMs = 200;
        private const int FrameMs = 15;

        internal Action<Action> SynchronizeSlideIn { private get; set; }

        public ToastForm(string title, string body, string slot = null, string email = null)
            : this(title, body, slot, email, false)
        {
        }

        internal ToastForm(string title, string body, string slot, string email, bool compact)
        {
            this.compact = compact;
            dpiScale = UiTheme.SystemDpiScale();
            int number = UiTheme.AccountNumber(slot);
            titleText = number > 0 ? L.F("账号 {0}", number) + (compact ? "" : "  ·  " + title) : title;
            string[] lines = body.Split(new[] { '\n', '｜' }, 2);
            bodyText = lines[0].Trim();
            resetText = lines.Length > 1 ? lines[1].Trim() : "";
            accountColor = UiTheme.AccountColor(slot);
            emailMask = email != null && email.Length <= 128 &&
                System.Text.RegularExpressions.Regex.IsMatch(email, @"^[A-Za-z0-9]{1,2}\*{3}@[A-Za-z0-9.\-]{1,123}$") ? email : "";
            smallFont = UiTheme.UiFont(8.5f);
            titleFont = UiTheme.UiFont(9.25f);
            bodyFont = UiTheme.UiFont(12f, FontStyle.Bold);

            Text = titleText;
            AccessibleName = titleText + " " + emailMask + " " + body;
            FormBorderStyle = FormBorderStyle.None;
            AutoScaleMode = AutoScaleMode.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            ClientSize = new Size(UiTheme.S(compact ? CardW / 2 : CardW, dpiScale),
                UiTheme.S(String.IsNullOrEmpty(resetText) ? 73 : 98, dpiScale));
            BackColor = SystemInformation.HighContrast ? SystemColors.Window : UiTheme.Surface;
            DoubleBuffered = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);

            life = new System.Windows.Forms.Timer();
            life.Interval = 7000;
            life.Tick += delegate { life.Stop(); Dismiss(); };

            // 位置固定：右下角，离屏幕边和任务栏各 16px；进场前先藏在屏幕右边缘外面
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            int edge = UiTheme.S(16, dpiScale);
            yPos = wa.Bottom - Height - edge;
            endX = wa.Right - Width - edge;
            Location = new Point(wa.Right, yPos);

            backdrop = new UiLiveBackdrop(delegate { return Bounds; }, PresentLayeredSurface);
            themeChanged = delegate { ApplyWindowTheme(); };
            UiTheme.Changed += themeChanged;
        }

        // 不抢焦点：弹出来的时候仍然可以继续打字
        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x08000000;  // WS_EX_NOACTIVATE
                if (layered) cp.ExStyle |= 0x00080000; // WS_EX_LAYERED
                return cp;
            }
        }

        private void ApplyWindowTheme()
        {
            BackColor = SystemInformation.HighContrast ? SystemColors.Window : UiTheme.Surface;
            if (IsHandleCreated && Visible && layered) PresentLayeredSurface();
            else Invalidate();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            backdrop.CaptureInitial(new Rectangle(endX, yPos, Width, Height));
            PresentLayeredSurface();
            if (layered && Environment.GetEnvironmentVariable("QUOTA_TOAST_STATIC") != "1") backdrop.Start(this);
            Action slideIn = delegate { if (!IsDisposed && !dismissing) StartAnim(true); };
            if (SynchronizeSlideIn == null) slideIn();
            else SynchronizeSlideIn(slideIn);
        }

        private void PresentLayeredSurface()
        {
            using (Bitmap surface = RenderSurface(backdrop.Frame))
                if (UiLayeredSurface.Present(Handle, surface, Location, 255)) return;

            backdrop.Stop();
            layered = false;
            RecreateHandle();
            UiTheme.ClipRoundedWindow(this, UiTheme.S(CornerRadius, dpiScale));
            Invalidate();
        }

        // 缓出曲线：起步快、收尾慢，跟系统通知的手感接近
        private void StartAnim(bool slideIn)
        {
            animIn = slideIn;
            animElapsed = 0;
            animDuration = slideIn ? SlideInMs : SlideOutMs;
            if (anim == null)
            {
                anim = new System.Windows.Forms.Timer();
                anim.Interval = FrameMs;
                anim.Tick += OnAnimTick;
            }
            anim.Start();
        }

        private void OnAnimTick(object sender, EventArgs e)
        {
            animElapsed += FrameMs;
            double t = animElapsed >= animDuration ? 1.0 : (double)animElapsed / animDuration;
            double eased = 1.0 - Math.Pow(1.0 - t, 3.0);

            if (animIn)
            {
                int x = (int)Math.Round(Screen.PrimaryScreen.WorkingArea.Right + (endX - Screen.PrimaryScreen.WorkingArea.Right) * eased);
                Location = new Point(x, yPos);
                if (t >= 1.0)
                {
                    anim.Stop();
                    Location = new Point(endX, yPos);
                    life.Start();
                }
            }
            else
            {
                int from = endX;
                int to = Screen.PrimaryScreen.WorkingArea.Right;
                Location = new Point((int)Math.Round(from + (to - from) * eased), yPos);
                if (t >= 1.0)
                {
                    anim.Stop();
                    Close();
                }
            }
        }

        private void Dismiss()
        {
            if (dismissing) return;
            dismissing = true;
            life.Stop();
            backdrop.Stop();
            StartAnim(false);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Dismiss();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (layered) return;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var outline = UiTheme.Round(new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f), UiTheme.S(CornerRadius, dpiScale)))
            using (var edge = new Pen(SystemInformation.HighContrast ? SystemColors.WindowFrame : UiTheme.Border, 1f))
                e.Graphics.DrawPath(edge, outline);
            PaintContent(e.Graphics);
        }

        internal Bitmap RenderSurface(Bitmap backdrop = null)
        {
            Rectangle area = new Rectangle(0, 0, Width, Height);
            using (var plate = new Bitmap(area.Width, area.Height, System.Drawing.Imaging.PixelFormat.Format24bppRgb))
            {
                using (Graphics graphics = Graphics.FromImage(plate))
                {
                    if (backdrop != null && !SystemInformation.HighContrast) UiBackdrop.PaintOverlay(graphics, area, backdrop);
                    else graphics.Clear(BackColor);
                }
                int radius = UiTheme.S(CornerRadius, dpiScale);
                using (Graphics graphics = Graphics.FromImage(plate))
                {
                    graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    RectangleF outline = new RectangleF(1, 1, Width - 2, Height - 2);
                    if (!SystemInformation.HighContrast) UiBackdrop.DrawGlassEdge(graphics, outline, radius - 1);
                    else
                    {
                        using (var shape = UiTheme.Round(outline, radius - 1))
                        using (var edge = new Pen(SystemColors.WindowFrame))
                            graphics.DrawPath(edge, shape);
                    }
                    PaintContent(graphics);
                }

                var surface = new Bitmap(area.Width, area.Height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
                using (Graphics alpha = Graphics.FromImage(surface))
                using (var shape = UiTheme.Round(area, radius))
                using (var fill = new TextureBrush(plate, WrapMode.Clamp))
                {
                    alpha.SmoothingMode = SmoothingMode.AntiAlias;
                    alpha.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    alpha.FillPath(fill, shape);
                }
                return surface;
            }
        }

        internal Bitmap RenderCurrentSurface() { return RenderSurface(layered ? backdrop.Frame : null); }

        private void PaintContent(Graphics g)
        {
            Color detailColor = SystemInformation.HighContrast ? SystemColors.WindowText :
                (UiTheme.Dark ? UiBackdrop.PopupTextColor : UiTheme.GlassMuted);
            Color textColor = SystemInformation.HighContrast ? SystemColors.WindowText : UiBackdrop.PopupTextColor;
            int barX = UiTheme.S(13, dpiScale), barW = Math.Max(3, UiTheme.S(3, dpiScale)), textLeft = UiTheme.S(TextInset, dpiScale);
            int rightPad = UiTheme.S(33, dpiScale), tailPad = UiTheme.S(16, dpiScale);
            UiTheme.Fill(g, new Rectangle(barX, barX, barW, ClientSize.Height - barX * 2), accountColor, null);

            int titleWidth = Math.Min(ClientSize.Width - textLeft - rightPad,
                TextRenderer.MeasureText(g, titleText, titleFont, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width + UiTheme.S(3, dpiScale));
            DrawToastText(g, titleText, titleFont, new Rectangle(textLeft, UiTheme.S(9, dpiScale), titleWidth, UiTheme.S(21, dpiScale)),
                textColor);
            int emailLeft = textLeft + titleWidth + UiTheme.S(8, dpiScale);
            if (emailMask.Length > 0 && emailLeft < ClientSize.Width - rightPad)
                DrawToastText(g, emailMask, smallFont, new Rectangle(emailLeft, UiTheme.S(9, dpiScale), ClientSize.Width - emailLeft - rightPad, UiTheme.S(21, dpiScale)),
                    detailColor);
            var bodyBounds = new Rectangle(textLeft, UiTheme.S(33, dpiScale), ClientSize.Width - textLeft - tailPad, UiTheme.S(28, dpiScale));
            DrawToastText(g, bodyText, bodyFont, bodyBounds, textColor, compact);
            if (!String.IsNullOrEmpty(resetText))
                DrawToastText(g, resetText, smallFont, new Rectangle(textLeft, UiTheme.S(65, dpiScale), ClientSize.Width - textLeft - tailPad, UiTheme.S(18, dpiScale)),
                    detailColor);

            int cx = ClientSize.Width - UiTheme.S(17, dpiScale), cy = UiTheme.S(19, dpiScale), arm = UiTheme.S(4, dpiScale);
            using (Pen pen = new Pen(detailColor, Math.Max(1.1f, 1.2f * dpiScale)))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.DrawLine(pen, cx - arm, cy - arm, cx + arm, cy + arm);
                g.DrawLine(pen, cx - arm, cy + arm, cx + arm, cy - arm);
            }
        }

        private static void DrawToastText(Graphics graphics, string text, Font font, Rectangle bounds, Color color, bool fit = false)
        {
            PixelOffsetMode previousOffset = graphics.PixelOffsetMode;
            graphics.PixelOffsetMode = PixelOffsetMode.Default;
            graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            try
            {
                using (var format = new StringFormat(StringFormat.GenericTypographic))
                using (var brush = new SolidBrush(color))
                {
                    format.LineAlignment = StringAlignment.Center;
                    format.Trimming = StringTrimming.EllipsisCharacter;
                    format.FormatFlags |= StringFormatFlags.NoWrap;
                    if (fit && !String.IsNullOrEmpty(text))
                    {
                        float width = graphics.MeasureString(text, font, PointF.Empty, format).Width;
                        if (width > bounds.Width)
                        {
                            using (var fitted = new Font(font.FontFamily, font.Size * (bounds.Width - 1) / width, font.Style))
                                graphics.DrawString(text, fitted, brush, bounds, format);
                            return;
                        }
                    }
                    graphics.DrawString(text, font, brush, bounds, format);
                }
            }
            finally { graphics.PixelOffsetMode = previousOffset; }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                UiTheme.Changed -= themeChanged;
                backdrop.Dispose();
                life.Dispose();
                if (anim != null) { anim.Dispose(); anim = null; }
                smallFont.Dispose();
                titleFont.Dispose();
                bodyFont.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    // 找本机的 codex.exe。顺序：环境变量 → 正在运行的进程 → LocalAppData → 商店应用 → PATH。
    // 正在运行的进程排第二，是因为 Codex 桌面版开着的时候这是最准的一条线索。
    internal static class CodexFinder
    {
        private const string StorePackageFamily = "OpenAI.Codex_2p2nqsd0c76g0";
        private const int InsufficientBuffer = 122;
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern int GetPackagesByPackageFamily(string family, ref uint count, IntPtr names, ref uint length, IntPtr buffer);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern int GetPackagePathByFullName(string name, ref uint length, StringBuilder path);

        public static string Find()
        {
            string p;
            p = FromEnv(); if (p != null) return p;
            p = FromRunning(); if (p != null) return p;
            p = FromLocalAppDataBin(); if (p != null) return p;
            p = FindPackagedCli(); if (p != null) return p;
            p = FromPath(); if (p != null) return p;
            return "";
        }

        private static string FromEnv()
        {
            string v = Environment.GetEnvironmentVariable("CODEX_CLI_PATH");
            return (!string.IsNullOrEmpty(v) && File.Exists(v)) ? v : null;
        }

        private static string FromRunning()
        {
            try
            {
                foreach (Process p in Process.GetProcesses())
                {
                    try
                    {
                        if (p.ProcessName.IndexOf("codex", StringComparison.OrdinalIgnoreCase) < 0) continue;
                        string path = null;
                        try { path = p.MainModule.FileName; } catch { }
                        if (!string.IsNullOrEmpty(path)
                            && String.Equals(Path.GetFileName(path), "codex.exe", StringComparison.OrdinalIgnoreCase)
                            && !File.Exists(Path.Combine(Path.GetDirectoryName(path), "resources", "codex.exe"))
                            && File.Exists(path))
                        {
                            return path;
                        }
                    }
                    catch { }
                    finally { try { p.Dispose(); } catch { } }
                }
            }
            catch { }
            return null;
        }

        private static string FromLocalAppDataBin()
        {
            try
            {
                string root = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    @"OpenAI\Codex\bin");
                return NewestIn(root);
            }
            catch { return null; }
        }

        internal static string FindPackagedCli()
        {
            IntPtr names = IntPtr.Zero, buffer = IntPtr.Zero;
            try
            {
                // Registered locations also cover non-default drives and restricted WindowsApps listings.
                uint count = 0, length = 0;
                if (GetPackagesByPackageFamily(StorePackageFamily, ref count, IntPtr.Zero, ref length, IntPtr.Zero) != InsufficientBuffer)
                    return null;
                names = Marshal.AllocHGlobal(checked((int)count * IntPtr.Size));
                buffer = Marshal.AllocHGlobal(checked((int)length * sizeof(char)));
                if (GetPackagesByPackageFamily(StorePackageFamily, ref count, names, ref length, buffer) != 0) return null;
                string best = null;
                Version newest = new Version();
                for (int i = 0; i < count; i++)
                {
                    string name = Marshal.PtrToStringUni(Marshal.ReadIntPtr(names, i * IntPtr.Size));
                    if (String.IsNullOrEmpty(name)) continue;
                    string[] parts = name.Split('_');
                    Version version;
                    if (parts.Length < 5 || !Version.TryParse(parts[1], out version) || version < newest) continue;
                    uint size = 0;
                    if (GetPackagePathByFullName(name, ref size, null) != InsufficientBuffer || size == 0) continue;
                    var path = new StringBuilder((int)size);
                    if (GetPackagePathByFullName(name, ref size, path) != 0) continue;
                    string candidate = Path.Combine(path.ToString(), @"app\resources\codex.exe");
                    if (!File.Exists(candidate)) continue;
                    best = candidate;
                    newest = version;
                }
                return best;
            }
            catch (EntryPointNotFoundException) { return null; }
            finally
            {
                if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer);
                if (names != IntPtr.Zero) Marshal.FreeHGlobal(names);
            }
        }

        internal static string BundledNode(string codexPath)
        {
            if (String.IsNullOrEmpty(codexPath) || !Path.IsPathRooted(codexPath) || !File.Exists(codexPath)) return null;
            string candidate = Path.Combine(Path.GetDirectoryName(codexPath), @"cua_node\bin\node.exe");
            return File.Exists(candidate) ? candidate : null;
        }

        // 用户指定一个文件夹时用：本层 → app\resources（商店版布局）→ 下一层子目录，取最新的
        public static string FindInFolder(string dir)
        {
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return null;

            string[] direct =
            {
                Path.Combine(dir, @"app\resources\codex.exe"),
                Path.Combine(dir, @"resources\codex.exe"),
                Path.Combine(dir, "codex.exe"),
            };
            foreach (string cand in direct)
            {
                if (File.Exists(cand)) return cand;
            }

            // 往下扫两层：够覆盖 bin\<hash>\codex.exe 和 商店版 app\resources\codex.exe，
            // 又不会在用户误选 C:\ 的时候把整块盘翻一遍
            string best = null;
            DateTime bestTime = DateTime.MinValue;
            int visited = 0;
            DateTime deadline = DateTime.Now.AddSeconds(5);
            System.Collections.Generic.Queue<string> queue = new System.Collections.Generic.Queue<string>();
            queue.Enqueue(dir);
            for (int depth = 0; depth <= 2 && queue.Count > 0; depth++)
            {
                int count = queue.Count;
                for (int i = 0; i < count; i++)
                {
                    if (visited++ > 800 || DateTime.Now > deadline) break;
                    string cur = queue.Dequeue();
                    string cand = Path.Combine(cur, "codex.exe");
                    if (File.Exists(cand))
                    {
                        DateTime time = File.GetLastWriteTimeUtc(cand);
                        if (time > bestTime) { bestTime = time; best = cand; }
                        continue;
                    }
                    try
                    {
                        foreach (string d in Directory.GetDirectories(cur)) queue.Enqueue(d);
                    }
                    catch { }
                }
            }
            return best;
        }

        private static string FromPath()
        {
            try
            {
                string pathVar = Environment.GetEnvironmentVariable("PATH");
                if (string.IsNullOrEmpty(pathVar)) return null;
                foreach (string part in pathVar.Split(';'))
                {
                    string dir = part.Trim();
                    if (dir.Length == 0) continue;
                    string cand = Path.Combine(dir, "codex.exe");
                    if (File.Exists(cand)) return cand;
                }
            }
            catch { }
            return null;
        }

        private static string NewestIn(string root)
        {
            if (!Directory.Exists(root)) return null;
            string best = null;
            DateTime bestTime = DateTime.MinValue;
            string direct = Path.Combine(root, "codex.exe");
            if (File.Exists(direct)) { best = direct; bestTime = File.GetLastWriteTimeUtc(direct); }
            try
            {
                foreach (string d in Directory.GetDirectories(root))
                {
                    string cand = Path.Combine(d, "codex.exe");
                    if (!File.Exists(cand)) continue;
                    DateTime time = File.GetLastWriteTimeUtc(cand);
                    if (time > bestTime) { bestTime = time; best = cand; }
                }
            }
            catch { }
            return best;
        }
    }

    // 找不到 Codex 时弹的识别界面：自动搜寻 或 手动指定 codex.exe
    internal class CodexLocateForm : UiWindow
    {
        public string SelectedPath = "";

        private readonly TextBox pathBox;
        private readonly Label status;
        private readonly Color bg;
        private readonly Color fg;
        private readonly Color sub;

        public CodexLocateForm(string current)
        {
            bg = SystemColors.Control;
            fg = SystemColors.ControlText;
            sub = SystemColors.GrayText;

            Text = "Codex";
            CanResize = false;
            StartPosition = FormStartPosition.CenterScreen;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowIcon = false;
            Font = UiTheme.UiFont(SystemFonts.MessageBoxFont.SizeInPoints);

            int inset = S(24), actionHeight = S(38), buttonGap = S(8), groupGap = S(40);
            int cap = ScaledCaptionHeight;

            string autoLabel = L.T("自动搜寻");
            string chooseLabel = L.T("选择…");
            string okLabel = L.T("确定");
            string cancelLabel = L.T("取消");
            int leftWidth = Math.Max(LogicalButtonWidth(autoLabel, Font, UiScale), LogicalButtonWidth(chooseLabel, Font, UiScale));
            int rightWidth = Math.Max(LogicalButtonWidth(okLabel, Font, UiScale), LogicalButtonWidth(cancelLabel, Font, UiScale));
            int clientWidth = Math.Max(S(520), 2 * inset + 2 * leftWidth + 2 * rightWidth + 2 * buttonGap + groupGap);
            int contentWidth = clientWidth - 2 * inset;

            BackColor = bg;
            ForeColor = fg;

            Label title = new UiLabel();
            title.Text = L.T(string.IsNullOrEmpty(current) || !File.Exists(current) ? "找不到 Codex" : "Codex 查询程序");
            title.Font = new Font(Font.FontFamily, 11f, FontStyle.Bold);
            title.Location = new Point(inset, S(14));
            title.AutoSize = true;
            title.BackColor = bg;
            title.ForeColor = fg;
            Controls.Add(title);

            Label hint = new UiLabel();
            hint.Name = "page-caption";
            hint.Text = L.T("选择 Codex 的安装文件夹或 Codex.exe 以连接程序。");
            hint.Location = new Point(inset, S(42));
            hint.Size = new Size(contentWidth, S(22));
            hint.BackColor = bg;
            hint.ForeColor = sub;
            Controls.Add(hint);

            pathBox = new AlphaTextBox();
            pathBox.BorderStyle = BorderStyle.None;
            pathBox.Text = current == null ? "" : current;
            pathBox.BackColor = SystemColors.Window;
            pathBox.ForeColor = fg;
            UiCard pathFrame = new UiCard();
            int boxFrameH = S(38);
            pathFrame.SetBounds(inset, S(72), contentWidth, boxFrameH);
            int boxHeight = pathBox.PreferredHeight;
            int boxPad = S(12);
            pathBox.Location = new Point(boxPad, Math.Max(S(4), (boxFrameH - boxHeight) / 2));
            pathBox.Width = contentWidth - boxPad * 2;
            pathFrame.Controls.Add(pathBox);
            Controls.Add(pathFrame);

            status = new UiLabel();
            status.Name = "page-caption";
            status.Text = "";
            status.Location = new Point(inset, S(118));
            status.AutoSize = true;
            status.MaximumSize = new Size(contentWidth, 0);
            status.BackColor = bg;
            status.ForeColor = sub;
            Controls.Add(status);

            int footerY = S(154);
            ClientSize = new Size(clientWidth, footerY + actionHeight + S(22) + cap);

            Button auto = new UiButton();
            auto.Name = "locate-auto";
            auto.Text = autoLabel;
            auto.Location = new Point(inset, footerY);
            auto.Size = new Size(leftWidth, actionHeight);
            auto.Click += delegate { AutoFind(); };
            Controls.Add(auto);

            Button choose = new UiButton();
            choose.Name = "locate-choose";
            choose.Text = chooseLabel;
            choose.Location = new Point(inset + leftWidth + buttonGap, footerY);
            choose.Size = new Size(leftWidth, actionHeight);
            choose.Click += delegate { Browse(); };
            Controls.Add(choose);

            Button ok = new UiButton();
            ok.Name = "locate-ok";
            ok.Text = okLabel;
            ok.Location = new Point(clientWidth - inset - 2 * rightWidth - buttonGap, footerY);
            ok.Size = new Size(rightWidth, actionHeight);
            ok.DialogResult = DialogResult.OK;
            ok.Click += delegate { ValidateAndClose(); };
            Controls.Add(ok);

            Button cancel = new UiButton();
            cancel.Name = "locate-cancel";
            cancel.Text = cancelLabel;
            cancel.Location = new Point(clientWidth - inset - rightWidth, footerY);
            cancel.Size = new Size(rightWidth, actionHeight);
            cancel.DialogResult = DialogResult.Cancel;
            Controls.Add(cancel);

            AcceptButton = ok;
            CancelButton = cancel;

            foreach(Control child in Controls) if(child != TitleBar) child.Top += cap;
            ((UiButton)ok).Accent = true;
            UiTheme.Hook(this);
        }

        private static int LogicalButtonWidth(string text, Font font, float dpiScale)
        {
            int px = TextRenderer.MeasureText(text, font, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width;
            return Math.Max(UiTheme.S(88, dpiScale), px + UiTheme.S(32, dpiScale));
        }

        private void AutoFind()
        {
            status.Text = L.T("正在搜寻…");
            Application.DoEvents();
            string found = CodexFinder.Find();
            if (string.IsNullOrEmpty(found))
            {
                status.Text = L.T("没找到，选个文件夹或直接选程序");
                return;
            }
            pathBox.Text = found;
            status.Text = L.T("已找到");
        }

        // 用户可能给文件夹，也可能给 exe。判断出来再处理，不给用户出选择题。
        // 路径合法返回 codex.exe 的完整路径，不合法返回 null
        private string Resolve(string input)
        {
            string p = input == null ? "" : input.Trim().Trim('"');
            if (p.Length == 0) return null;
            if (Directory.Exists(p)) return CodexFinder.FindInFolder(p);
            if (File.Exists(p)) return p;
            return null;
        }

        // 一个对话框同时收文件和文件夹：
        // 选中 exe 就直接返回它；进入某个文件夹后直接点「打开」，返回的是目录路径。
        private void Browse()
        {
            using (OpenFileDialog dlg = new OpenFileDialog())
            {
                dlg.Title = L.T("选择 Codex 的安装文件夹或 Codex.exe");
                dlg.Filter = "codex.exe|codex.exe|" + L.T("所有程序") + "|*.exe|" + L.T("所有文件") + "|*.*";
                dlg.CheckFileExists = false;    // 允许选中文件夹
                dlg.CheckPathExists = true;
                dlg.ValidateNames = false;
                dlg.FileName = L.T("选择此文件夹");

                string cur = pathBox.Text == null ? "" : pathBox.Text.Trim();
                if (cur.Length > 0)
                {
                    string dir = null;
                    try { dir = Directory.Exists(cur) ? cur : Path.GetDirectoryName(cur); } catch { }
                    if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir)) dlg.InitialDirectory = dir;
                }

                if (dlg.ShowDialog() != DialogResult.OK) return;

                string resolved = Resolve(dlg.FileName);
                if (string.IsNullOrEmpty(resolved))
                {
                    status.Text = L.T("这里没找到 codex.exe");
                    return;
                }
                pathBox.Text = resolved;
                status.Text = L.T("已找到");
            }
        }

        private void ValidateAndClose()
        {
            string resolved = Resolve(pathBox.Text);
            if (string.IsNullOrEmpty(resolved))
            {
                MessageBox.Show(this, L.T("这里没找到 codex.exe，换个位置试试。"),
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                DialogResult = DialogResult.None;
                return;
            }
            SelectedPath = resolved;
        }
    }

    internal class TrayContext : ApplicationContext
    {        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool DestroyIcon(IntPtr handle);


        private const int PrimaryEverySec = 5;     // 5 小时额度：每 5 秒
        private const int SecondaryEverySec = 60;  // 7 天额度：每 1 分钟

        // 详情窗配色（每次打开时读系统主题，浅色/深色自动跟随）
        private bool detailDark;
        private Color detailBg;
        private Color detailFg;

        [DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(IntPtr hwnd);

        [DllImport("dwmapi.dll")]
        internal static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        internal static void SetDarkTitleBar(IntPtr hwnd)
        {
            try
            {
                int one = 1;
                // 20 = DWMWA_USE_IMMERSIVE_DARK_MODE（Win10 1903+），19 是更早的编号
                if (DwmSetWindowAttribute(hwnd, 20, ref one, sizeof(int)) != 0)
                {
                    DwmSetWindowAttribute(hwnd, 19, ref one, sizeof(int));
                }
            }
            catch { }
        }

        internal static bool IsDarkMode()
        {
            try
            {
                using (Microsoft.Win32.RegistryKey k =
                    Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                        @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    if (k != null)
                    {
                        object v = k.GetValue("AppsUseLightTheme");
                        if (v is int) return ((int)v) == 0;
                    }
                }
            }
            catch { }
            return false;
        }

        // 开机自启动：写在 HKCU\...\Run 里，指向当前 exe
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RunValueName = "CodexQuotaTray";

        private static string AutoStartValue()
        {
            return "\"" + Application.ExecutablePath + "\" --background";
        }

        private static bool IsAutoStartEnabled()
        {
            try
            {
                using (Microsoft.Win32.RegistryKey k =
                    Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKeyPath))
                {
                    if (k == null) return false;
                    object v = k.GetValue(RunValueName);
                    if (v == null) return false;
                    string current = Convert.ToString(v).Trim();
                    return string.Equals(current, AutoStartValue(), StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(current, "\"" + Application.ExecutablePath + "\"", StringComparison.OrdinalIgnoreCase);
                }
            }
            catch
            {
                return false;
            }
        }

        private static bool SetAutoStart(bool on)
        {
            try
            {
                using (Microsoft.Win32.RegistryKey k =
                    Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKeyPath, true))
                {
                    if (k == null) return false;
                    if (on)
                    {
                        k.SetValue(RunValueName, AutoStartValue());
                    }
                    else if (k.GetValue(RunValueName) != null)
                    {
                        k.DeleteValue(RunValueName, false);
                    }
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void UpdateAutoStartItem(ToolStripMenuItem item)
        {
            item.Text = IsAutoStartEnabled() ? L.T("关闭开机自启动") : L.T("开启开机自启动");
        }

        // 右键菜单的深色配色（浅色模式不用它，走系统默认）
        private sealed class DarkMenuColors : ProfessionalColorTable
        {
            public override Color ToolStripDropDownBackground { get { return Color.FromArgb(32, 32, 32); } }
            public override Color MenuItemSelected { get { return Color.FromArgb(58, 58, 64); } }
            public override Color MenuItemSelectedGradientBegin { get { return Color.FromArgb(58, 58, 64); } }
            public override Color MenuItemSelectedGradientEnd { get { return Color.FromArgb(58, 58, 64); } }
            public override Color MenuItemBorder { get { return Color.FromArgb(72, 72, 80); } }
            public override Color MenuBorder { get { return Color.FromArgb(64, 64, 70); } }
            public override Color SeparatorDark { get { return Color.FromArgb(58, 58, 64); } }
            public override Color SeparatorLight { get { return Color.FromArgb(44, 44, 48); } }
            public override Color ImageMarginGradientBegin { get { return Color.FromArgb(32, 32, 32); } }
            public override Color ImageMarginGradientMiddle { get { return Color.FromArgb(32, 32, 32); } }
            public override Color ImageMarginGradientEnd { get { return Color.FromArgb(32, 32, 32); } }
            // 勾选块：默认是浅色底 + 深色勾，深色菜单下等于看不见，这里换成深蓝底
            public override Color CheckBackground { get { return Color.FromArgb(52, 96, 156); } }
            public override Color CheckSelectedBackground { get { return Color.FromArgb(64, 116, 186); } }
            public override Color CheckPressedBackground { get { return Color.FromArgb(64, 116, 186); } }
        }

        internal static ContextMenuStrip CreateNativeContextMenu()
        {
            ContextMenuStrip menu = new UiTrayMenu { ShowImageMargin = false, ShowItemToolTips = true, Font = UiTheme.UiFont(9.5f) };
            return menu;
        }

        private static void ApplyMenuTheme(ContextMenuStrip menu)
        {
            if (IsDarkMode())
            {
                menu.Renderer = new ToolStripProfessionalRenderer(new DarkMenuColors());
                menu.BackColor = Color.FromArgb(32, 32, 32);
                menu.ForeColor = Color.FromArgb(232, 232, 232);
            }
            else
            {
                menu.Renderer = new ToolStripProfessionalRenderer();
                menu.BackColor = SystemColors.Control;
                menu.ForeColor = SystemColors.ControlText;
            }

            ThemeMenuItems(menu.Items, IsDarkMode(), menu.ForeColor);
        }

        // 递归给子菜单上色。
        // 之前只给顶层上色，"额度提醒"那个子菜单仍沿用系统浅色，
        // 深色主题下就成了深底深字，几乎看不见。
        private static void ThemeMenuItems(ToolStripItemCollection items, bool dark, Color fg)
        {
            foreach (ToolStripItem item in items)
            {
                item.BackColor = Color.Transparent;
                item.ForeColor = fg;
                ToolStripMenuItem mi = item as ToolStripMenuItem;
                if (mi == null || mi.DropDownItems.Count == 0) continue;

                mi.DropDown.BackColor = dark ? Color.FromArgb(32, 32, 32) : SystemColors.Control;
                mi.DropDown.ForeColor = fg;
                mi.DropDown.Renderer = dark
                    ? (ToolStripRenderer)new ToolStripProfessionalRenderer(new DarkMenuColors())
                    : (ToolStripRenderer)new ToolStripProfessionalRenderer();
                ThemeMenuItems(mi.DropDownItems, dark, fg);
            }
        }

        private readonly string root;
        private readonly string logPath;
        private readonly string quotaJs;
        private string nodeExe;

        private readonly NotifyIcon tray;
        private readonly System.Windows.Forms.Timer uiTimer;
        private readonly ManualResetEvent refreshNow = new ManualResetEvent(false);
        private readonly ManualResetEvent stopWorker = new ManualResetEvent(false);
        private readonly object gate = new object();
        private readonly Thread worker;

        private QuotaResult pending;      // 后台线程放结果，UI 线程取
        private bool hasPending;

        private bool hasData;
        private string plan = "";
        private double primaryUsed = -1;
        private double primaryReset;
        private double secondaryUsed = -1;
        private double secondaryReset;
        private int secondaryDays = 7;
        private DateTime secondaryShownAt = DateTime.MinValue;
        private DateTime lastOkAt = DateTime.MinValue;
        private string lastError = "";
        private string lastLogged = "";
        private string lastWindows = "";

        private Icon currentIcon;

        // 额度提醒
        private AppSettings settings = new AppSettings();
        private NotifyWindow notifyTiered;
        private NotifyWindow notifyFixed;
        private DailyPaceTracker dailyPace;
        private Font menuFontBold;

        // 菜单项留引用：切语言时要就地改标题，不能重建整个菜单
        private ToolStripMenuItem menuRefresh, menuDetail, menuNotify;
        private ToolStripMenuItem menuAutoStart, menuShortcut, menuExit, menuLang, menuAccounts, menuDisplay, menuAccountHeader;
        private AccountConnectionForm accountForm;
        private QuotaSettingsForm quotaSettingsForm;
        private readonly ReleaseUpdates releaseUpdates;
        private MultiAccountController multiAccounts;
        private volatile bool multiMode;
        private bool accountDiscoveryPending;
        private bool pendingDetails;
        private bool contextDisposed;
        private string multiDiscoveryError;
        private readonly AccountToastQueue accountToasts = new AccountToastQueue();
        private string viewAccountLabel = "";
        private string viewAccountSlot;
        private readonly EventWaitHandle accountsRequest = new EventWaitHandle(false, EventResetMode.AutoReset, "CodexQuotaTrayAccountsRequest");
        private readonly EventWaitHandle settingsRequest = new EventWaitHandle(false, EventResetMode.AutoReset, "CodexQuotaTraySettingsRequest");
        private readonly EventWaitHandle displayRequest = new EventWaitHandle(false, EventResetMode.AutoReset, "CodexQuotaTrayDisplayRequest");
        private readonly EventWaitHandle detailsRequest = new EventWaitHandle(false, EventResetMode.AutoReset, "CodexQuotaTrayDetailsRequest");
        private readonly EventWaitHandle exitRequest = new EventWaitHandle(false, EventResetMode.AutoReset, "CodexQuotaTrayExitRequest");
        private readonly EventWaitHandle groupTestRequest = new EventWaitHandle(false, EventResetMode.AutoReset, "CodexQuotaTrayGroupTestRequest");
        private ToolStripMenuItem[] menuLevels = new ToolStripMenuItem[4];
        private ToolStripMenuItem[] menuLangItems = new ToolStripMenuItem[4];
        private static readonly int[] LevelValues = new int[] { -1, 0, 1, 2 };
        private static readonly string[] LangValues = new string[] { "auto", "zh", "ja", "en" };

        // 自绘提醒窗与提示音
        private ToastForm toast;
        private static System.Collections.Generic.Dictionary<string, System.Media.SoundPlayer> players;
        private static System.Media.SoundPlayer fallbackToastSound;

        // 调试预览模式：绝不碰桌面快捷方式和桌面图标
        private bool noDesktopTouch;
        private bool needLocate;
        private bool locateShown;

        // 桌面图标同步
        private string desktopIconKey = "";
        private string desktopIconLink;
        private int desktopIconSeq = 0;
        private readonly string desktopIconRunTag = DateTime.Now.ToString("MMddHHmmss");

        public TrayContext()
        {
            // 绿色版：数据优先放 exe 同目录（真便携）；目录不可写才退到 %APPDATA%。
            // 当前安装的 exe 本来就在 ~/.codex/quota-tray 下，所以这对老部署完全兼容。
            root = ResolveDataDir();
            releaseUpdates = new ReleaseUpdates(Application.ExecutablePath, root, Log);
            try { Directory.CreateDirectory(root); } catch { }
            logPath = Path.Combine(root, "tray.log");
            settings = AppSettings.Load(root);
            UiTheme.Set(settings.theme);
            if (IsAutoStartEnabled()) SetAutoStart(true);
            ApplyLanguageSetting();
            PrepareCodexPath();
            quotaJs = Path.Combine(root, "quota.js");
            EnsureQuotaJs();       // 单文件 exe：js 内嵌在程序里，缺了就解出来
            if (settings.HasCodex || AccountConnectionForm.HasBindingStore()) nodeExe = FindNode(settings.codexPath);

            tray = new NotifyIcon();
            tray.Visible = true;
            tray.Icon = DrawIcon(0, 0, false);
            currentIcon = tray.Icon;
            tray.Text = ClampTray(L.T("未绑定任何账号"));

            var menu = CreateNativeContextMenu();
            menuFontBold = new Font(SystemFonts.MenuFont, FontStyle.Bold);

            menuRefresh = new ToolStripMenuItem();
            menuRefresh.Click += delegate { if (multiAccounts != null) multiAccounts.ReloadAndRefresh(); else RestartMultiAccounts(); };

            menuDetail = new ToolStripMenuItem();
            menuDetail.Click += delegate { if (multiMode) multiAccounts.ShowAllDetails(); else ShowDetail(); };

            // 额度提醒：档位直接决定 5 小时额度（Pro 则是 7 天额度）在哪些节点响。
            menuNotify = new ToolStripMenuItem();
            for (int i = 0; i < LevelValues.Length; i++)
            {
                ToolStripMenuItem mi = new ToolStripMenuItem();
                mi.Click += delegate { ShowSettingsPage("notifications"); };
                menuLevels[i] = mi;
                menuNotify.DropDownItems.Add(mi);
            }

            menuAutoStart = new ToolStripMenuItem();
            menuAutoStart.Click += delegate
            {
                bool target = !IsAutoStartEnabled();
                if (!SetAutoStart(target))
                {
                    ShowBalloon(L.T("设置失败"), L.T("写注册表没成功，可能是权限问题。"));
                    UpdateAutoStartItem(menuAutoStart);
                    return;
                }
                UpdateAutoStartItem(menuAutoStart);
                ShowBalloon(L.T(target ? "已开启开机自启动" : "已关闭开机自启动"),
                    L.T(target ? "下次登录 Windows 就会自动启动。" : "下次登录不会再自动启动。"));
                Log("开机自启动 -> " + (target ? "开" : "关"));
            };

            menuAccountHeader = new ToolStripMenuItem();
            menuAccountHeader.Enabled = false;
            menuAccountHeader.Visible = false;
            menuDisplay = new ToolStripMenuItem();
            menuDisplay.Click += delegate { ShowSettingsPage("general"); };
            menuAccounts = new ToolStripMenuItem();
            menuAccounts.Click += delegate { ShowSettingsPage("accounts"); };


            menuShortcut = new ToolStripMenuItem();
            menuShortcut.Click += delegate
            {
                if (multiMode) { multiAccounts.CreateDesktopShortcut(); return; }
                if (EnsureDesktopShortcut(true))
                    ShowBalloon(L.T("已创建"), L.T("桌面快捷方式已就位，图标会跟着额度变化。"));
            };

            menuExit = new ToolStripMenuItem();
            menuExit.Name = "tray-exit";
            menuExit.Click += delegate { Shutdown(); };

            // 语言：自动 = 跟随系统，其余两种手动锁死
            menuLang = new ToolStripMenuItem();
            for (int i = 0; i < LangValues.Length; i++)
            {
                string v = LangValues[i];
                ToolStripMenuItem mi = new ToolStripMenuItem();
                mi.Click += delegate { SetLanguage(v); };
                menuLangItems[i] = mi;
                menuLang.DropDownItems.Add(mi);
            }

            menu.Items.Add(menuRefresh);
            menu.Items.Add(menuDisplay);
            menu.Items.Add(menuExit);

            ApplyLanguage();
            UpdateAutoStartItem(menuAutoStart);

            menu.Opening += delegate {
                HideQuotaTip();
                ((UiTrayMenu)menu).StatusText = multiMode ? null : L.T(AccountStatusKey());
                UpdateAutoStartItem(menuAutoStart);
                // 子菜单是另一个 ToolStrip，得单独开，否则档位那几条不出悬停提示
                menuNotify.DropDown.ShowItemToolTips = true;
                for (int i = 0; i < menuLevels.Length; i++)
                {
                    bool on = (LevelValues[i] == settings.notifyLevel);
                    menuLevels[i].Checked = on;
                    // 加粗 + 勾选双保险：深色菜单里那个勾不一定看得清
                    menuLevels[i].Font = on ? menuFontBold : SystemFonts.MenuFont;
                }
                for (int i = 0; i < menuLangItems.Length; i++)
                {
                    bool on = (LangValues[i] == settings.language);
                    menuLangItems[i].Checked = on;
                    menuLangItems[i].Font = on ? menuFontBold : SystemFonts.MenuFont;
                }
            };
            tray.ContextMenuStrip = menu;
            trayMenu = (UiTrayMenu)menu;
            ((UiTrayMenu)menu).Track(tray);

            // 调试用：--preview-menu <png> 把右键菜单展开并截图，检查深色下的对比度
            string[] menuArgv = Environment.GetCommandLineArgs();
            if (menuArgv.Length > 2 && menuArgv[1] == "--preview-menu")
            {
                string shotPath = menuArgv[2];
                System.Windows.Forms.Timer t1 = new System.Windows.Forms.Timer();
                t1.Interval = 500;
                t1.Tick += delegate
                {
                    t1.Stop();
                    menu.Show(new Point(220, 220));
                    System.Windows.Forms.Timer t2 = new System.Windows.Forms.Timer();
                    t2.Interval = 600;
                    t2.Tick += delegate
                    {
                        t2.Stop();
                        try
                        {
                            Rectangle r = menu.Bounds;
                            if (menuNotify.DropDown.Visible)
                                r = Rectangle.Union(r, menuNotify.DropDown.Bounds);
                            r.Inflate(12, 12);
                            int x = Math.Max(0, r.Left), y = Math.Max(0, r.Top);
                            int w = Math.Min(r.Width, 1600), h = Math.Min(r.Height, 1200);
                            using (var bmp = new Bitmap(w, h))
                            using (var g = Graphics.FromImage(bmp))
                            {
                                g.CopyFromScreen(x, y, 0, 0, new Size(w, h));
                                bmp.Save(shotPath, ImageFormat.Png);
                            }
                        }
                        catch { }
                        ExitThread();
                    };
                    t2.Start();
                };
                t1.Start();
            }

            // 双击图标 = 刷新
            tray.DoubleClick += delegate { if (multiAccounts != null) multiAccounts.RefreshAll(); else RestartMultiAccounts(); };

            Log("启动（pid=" + Process.GetCurrentProcess().Id + "，node=" + nodeExe + "）");

            // 调试用：加 --preview-detail 直接弹详情窗（假数据），方便检查样式
            string[] argv = Environment.GetCommandLineArgs();
            if (argv.Length > 1 && argv[1] == "--preview-detail")
            {
                plan = "plus";
                primaryUsed = 3;
                primaryReset = ToUnix(DateTime.UtcNow.AddHours(2));
                secondaryUsed = 82;
                secondaryReset = ToUnix(DateTime.UtcNow.AddDays(3));
                secondaryDays = 7;
                hasData = true;
                ShowDetailPreview();
                ExitThread();
                return;
            }

            // 第一次运行时自动建桌面快捷方式（绿色版的"开箱即用"）：
            // 只在首次做一次，之后用户自己删掉了就不再自动重建（可在右键菜单手动建）。
            string firstRunMark = Path.Combine(root, ".first-run-done");
            // 预览模式跑在临时目录里，绝不能让桌面快捷方式指过去
            noDesktopTouch = argv.Length > 1 && argv[1] == "--preview-menu";
            if (!noDesktopTouch && !File.Exists(firstRunMark))
            {
                if (EnsureDesktopShortcut(true))
                {
                    ShowBalloon(L.T("额度显示器已就绪"),
                        L.T("桌面图标会跟着额度一起变。右键托盘图标可查看详情。"));
                }
                try { File.WriteAllText(firstRunMark, DateTime.Now.ToString("s")); } catch { }
            }

            // 开机/重启时先看上次的桌面图标还能不能直接用：
            // 数值没变就不重画、不改快捷方式、不通知 Shell，桌面一个像素都不动。
            LoadDesktopIconState();
            if (!noDesktopTouch) RestartMultiAccounts();

            uiTimer = new System.Windows.Forms.Timer();
            uiTimer.Interval = 1000;
            uiTimer.Tick += delegate
            {
                if (exitRequest.WaitOne(0)) { Shutdown(); return; }
                if (multiAccounts != null) multiAccounts.Pump();
                if (accountsRequest.WaitOne(0)) ShowAccountConnections();
                if (settingsRequest.WaitOne(0)) ShowSettingsPage("accounts");
                if (displayRequest.WaitOne(0)) ShowDisplaySettings();
                if (groupTestRequest.WaitOne(0) && multiAccounts != null) multiAccounts.RunGroupUiTest();
                if (detailsRequest.WaitOne(0)) pendingDetails = true;
                if (pendingDetails && !accountDiscoveryPending)
                {
                    pendingDetails = false;
                    if (multiMode) multiAccounts.ShowAllDetails(); else ShowDetail();
                }
                PumpResults();
                PumpAccountToasts();
                if (!noDesktopTouch) PumpReleaseUpdates();
            };
            uiTimer.Start();
            if (!noDesktopTouch)
            {
                ConsumeUpdatedNotice();
                releaseUpdates.Check(false);
            }

            worker = new Thread(WorkerLoop);
            worker.IsBackground = true;
            worker.Start();
            if (argv.Length > 1 && argv[1] == "--connect-accounts") accountsRequest.Set();
            if (argv.Length == 1 || (argv.Length > 1 && argv[1] == "--settings")) settingsRequest.Set();
            if (argv.Length > 1 && argv[1] == "--display-settings") displayRequest.Set();
            if (argv.Length > 1 && argv[1] == "--quota-details") detailsRequest.Set();
            if (argv.Length > 1 && argv[1] == "--test-group-ui") groupTestRequest.Set();
        }

        private void ShowDisplaySettings() { ShowSettingsPage("display"); }
        private void ConsumeUpdatedNotice()
        {
            Version updated = ReleaseUpdates.ConsumeUpdatedVersion(root);
            if (updated == null) return;
            accountToasts.Enqueue(null, L.T("额度显示器"), L.F("已更新到 {0}", updated), NoSound, null, true, true);
        }
        private bool IsUiBusyForUpdate()
        {
            if ((quotaSettingsForm != null && !quotaSettingsForm.IsDisposed) ||
                (accountForm != null && !accountForm.IsDisposed) ||
                (multiAccounts != null && multiAccounts.HasOpenWindows) ||
                (trayMenu != null && trayMenu.PopupVisible) ||
                (tray != null && tray.ContextMenuStrip != null && tray.ContextMenuStrip.Visible) ||
                (quotaTip != null && !quotaTip.IsDisposed && quotaTip.Visible) ||
                (toast != null && !toast.IsDisposed)) return true;
            foreach (Form form in Application.OpenForms)
                if (form != null && !form.IsDisposed && form.Visible) return true;
            return false;
        }
        private void PumpReleaseUpdates()
        {
            releaseUpdates.Check(false);
            releaseUpdates.StartAutoDownload(settings.autoUpdate);
            if (releaseUpdates.TryAutoInstall(settings.autoUpdate, IsUiBusyForUpdate())) return;
            if (!settings.autoUpdate)
            {
                Version found = releaseUpdates.TakeNotification();
                if (found == null) return;
                try { ShowBalloon(L.T("有新版本"), L.F("最新版本 {0}", found)); }
                catch (Exception error) { Log("版本通知失败：" + error.Message); }
            }
        }
        internal void RefreshSettingsAccounts()
        {
            if (quotaSettingsForm != null && !quotaSettingsForm.IsDisposed) quotaSettingsForm.RefreshAccounts();
        }
        internal void ShowSettingsPage(string page)
        {
            releaseUpdates.Check(false);
            if (quotaSettingsForm != null && !quotaSettingsForm.IsDisposed)
            {
                quotaSettingsForm.Navigate(page); UiTheme.Present(quotaSettingsForm); return;
            }
            var actions = new SettingsActions();
            actions.Read = delegate {
                return new SettingsSnapshot { Accounts = multiAccounts == null ? new System.Collections.Generic.List<SettingsAccount>() : multiAccounts.SettingsAccounts(),
                    AccountsLoading = accountDiscoveryPending, DesktopSlot = multiAccounts == null ? "auto" : multiAccounts.DesktopPreference,
                    CodexMissing = !settings.HasCodex, AccountReadError = multiDiscoveryError,
                    Language = settings.language, Theme = settings.theme, DefaultNotifyLevel = settings.notifyLevel,
                    NotifySound = settings.notifySound, NotifyVolume = settings.notifyVolume,
                    DefaultDailyPace = settings.dailyPaceDefault,
                    AutoUpdate = settings.autoUpdate, AutoStart = IsAutoStartEnabled() };
            };
            actions.ConnectAccounts = delegate { ShowAccountConnections(); };
            actions.RefreshAll = delegate { if (multiAccounts != null) multiAccounts.RefreshAll(); else RestartMultiAccounts(); };
            actions.LocateCodex = delegate { ShowLocateDialog(); };
            actions.CreateShortcut = delegate { if (multiMode && multiAccounts != null) multiAccounts.CreateDesktopShortcut(); else EnsureDesktopShortcut(true); };
            actions.Exit = delegate { Shutdown(); };
            actions.SaveDisplay = delegate(string[] slots, string desktop) { return multiAccounts != null && multiAccounts.SaveDisplay(slots, desktop); };
            actions.SetAccountLevel = delegate(string slot, int level) {
                settings.SetAccountLevel(slot, level); settings.Save(root); accountToasts.Remove(slot);
                if (multiAccounts != null) multiAccounts.ResetReminder(slot);
            };
            actions.ApplyGlobalLevel = ApplyGlobalNotifyLevel;
            actions.ApplyGlobalDailyPace = delegate(bool enabled)
            {
                return settings.ApplyGlobalDailyPace(root, enabled);
            };
            actions.SetAccountDailyPace = delegate(string slot, bool enabled)
            {
                return settings.SetAccountDailyPace(root, slot, enabled);
            };
            actions.SetNotifySound = delegate(bool enabled)
            {
                if (!settings.SetNotifySound(root, enabled)) return false;
                if (!enabled) StopToastSound();
                return true;
            };
            actions.SetNotifyVolume = delegate(int volume)
            {
                if (!settings.SetNotifyVolume(root, volume)) return false;
                if (volume == 0) StopToastSound();
                return true;
            };
            actions.TestNotifyVolume = delegate(int volume) { PlayNotifySample(root, volume, Log); };
            actions.SetLanguage = delegate(string language) { SetLanguage(language); if (quotaSettingsForm != null && !quotaSettingsForm.IsDisposed) quotaSettingsForm.Reload(); };
            actions.SetTheme = delegate(string theme) { settings.theme = UiTheme.Normalize(theme); settings.Save(root); UiTheme.Set(settings.theme); };
            actions.SetAutoStart = delegate(bool enabled) { return SetAutoStart(enabled); };
            actions.SetAutoUpdate = delegate(bool enabled) { return settings.SetAutoUpdate(root, enabled); };
            actions.ReadUpdate = delegate { return releaseUpdates.State; };
            actions.CheckUpdate = delegate { releaseUpdates.Check(true); };
            actions.ApplyUpdate = releaseUpdates.Install;
            quotaSettingsForm = new QuotaSettingsForm(actions, page);
            quotaSettingsForm.FormClosed += delegate { quotaSettingsForm = null; };
            UiTheme.Present(quotaSettingsForm);
        }

        private void ShowAccountConnections()
        {
            if (accountForm != null && !accountForm.IsDisposed)
            {
                UiTheme.Present(accountForm);
                return;
            }
            try
            {
                if (nodeExe == null) nodeExe = FindNode(settings.codexPath);
                accountForm = new AccountConnectionForm(nodeExe, settings.codexPath, root, multiAccounts == null ? null : multiAccounts.SettingsAccounts());
                if (quotaSettingsForm != null && quotaSettingsForm.Visible) accountForm.Owner = quotaSettingsForm;
                accountForm.InitialSetup = !settings.bindingSetupDone;
                accountForm.BindingChanged += delegate { if (multiAccounts != null) multiAccounts.ReloadBindings(); else RestartMultiAccounts(); };
                accountForm.LocateRequested += delegate { locateShown = true; ShowLocateDialog(true); };
                accountForm.FormClosed += delegate
                {
                    accountForm = null;
                    // Pick up any binding written while the window was open, without restarting.
                    if (multiAccounts != null) multiAccounts.ReloadBindings();
                };
                UiTheme.Present(accountForm);
            }
            catch
            {
                ShowBalloon(L.T("账号连接"), L.T("无法打开账号连接，请重试。"));
            }
        }

        private void RestartMultiAccounts()
        {
            multiDiscoveryError = null;
            if (multiAccounts != null)
            {
                SetMultiMode(false);
                try { multiAccounts.Dispose(); } catch { }
                multiAccounts = null;
            }
            if (!AccountConnectionForm.HasBindingStore())
            {
                MultiDiscoveryComplete();
                return;
            }
            accountDiscoveryPending = true;
            if (nodeExe == null) nodeExe = FindNode(settings.codexPath);
            try
            {
                multiAccounts = new MultiAccountController(this, root, nodeExe, settings.codexPath, settings, tray.ContextMenuStrip);
            }
            catch
            {
                ReportMultiDiscoveryFailure();
            }
        }

        private string AccountStatusKey()
        {
            if (accountDiscoveryPending) return "正在读取本地绑定…";
            return multiDiscoveryError ?? "未绑定任何账号";
        }

        private void PublishAccountStatus()
        {
            if (!multiMode) tray.Text = ClampTray(L.T(AccountStatusKey()) +
                (settings.HasCodex ? "" : "\n" + L.T("未识别到 Codex")));
            RefreshSettingsAccounts();
        }

        internal void MultiDiscoveryComplete()
        {
            accountDiscoveryPending = false;
            multiDiscoveryError = null;
            if (multiMode && !settings.bindingSetupDone) { settings.bindingSetupDone = true; settings.Save(root); }
            PublishAccountStatus();
        }

        internal void ReportMultiDiscoveryFailure()
        {
            accountDiscoveryPending = false;
            multiDiscoveryError = "无法读取本地绑定，请手动刷新。";
            PublishAccountStatus();
        }

        private void PumpAccountToasts()
        {
            AccountToastEntry next = accountToasts.Take(toast != null && !toast.IsDisposed);
            if (next == null) return;
            string title = next.PlainTitle ? next.Label : (next.Slot == null ? next.Label + " · " + L.T("额度提醒") : L.T("额度提醒"));
            ShowToast(title, next.Body, next.Sound, next.Slot, next.EmailMask, next.Compact);
        }

        internal void SetMultiMode(bool enabled)
        {
            multiMode = enabled;
            if (enabled) MultiDiscoveryComplete();
            tray.Visible = !enabled;
            menuAccountHeader.Visible = enabled;
            if (!enabled)
            {
                accountToasts.Clear();
                viewAccountLabel = "";
                viewAccountSlot = null;
                notifyTiered = null;
                notifyFixed = null;
                refreshNow.Set();
            }
        }

        internal void PrepareMultiMenu(string slot, string label)
        {
            menuAccountHeader.Text = label;
            menuAccountHeader.Visible = true;
        }

        private static bool ValidMultiWindow(WindowInfo value)
        {
            return value != null && !double.IsNaN(value.used) && !double.IsInfinity(value.used) && value.used >= 0 && value.used <= 100 &&
                !double.IsNaN(value.windowMins) && !double.IsInfinity(value.windowMins) && value.windowMins > 0 &&
                !double.IsNaN(value.resetsAt) && !double.IsInfinity(value.resetsAt) && value.resetsAt >= 0 && value.resetsAt <= 253402300799;
        }

        private static void MultiWindows(ConnectionReply reply, out WindowInfo shortWindow, out WindowInfo longWindow)
        {
            shortWindow = null;
            longWindow = null;
            if (reply == null || !reply.ok) return;
            foreach (WindowInfo value in new WindowInfo[] { reply.primary, reply.secondary })
            {
                if (!ValidMultiWindow(value)) continue;
                if (QuotaWindow.IsLong(value.windowMins))
                {
                    if (longWindow == null || value.windowMins > longWindow.windowMins) longWindow = value;
                }
                else if (shortWindow == null || value.windowMins < shortWindow.windowMins) shortWindow = value;
            }
        }

        internal static ConnectionReply NormalizeMultiReply(ConnectionReply reply)
        {
            WindowInfo shortWindow, longWindow;
            MultiWindows(reply, out shortWindow, out longWindow);
            return new ConnectionReply
            {
                @event = "result", ok = shortWindow != null || longWindow != null,
                slot = reply == null ? null : reply.slot,
                identity = reply == null ? null : reply.identity,
                at = reply == null ? 0 : reply.at,
                primary = shortWindow, secondary = longWindow,
                code = shortWindow == null && longWindow == null ? "no_quota_data" : null
            };
        }

        internal static Icon MakeMultiIcon(ConnectionReply reply)
        {
            WindowInfo shortWindow, longWindow;
            MultiWindows(reply, out shortWindow, out longWindow);
            if (shortWindow == null && longWindow == null) return DrawIcon(0, 0, false);
            if (shortWindow == null) return DrawSingleIcon(QuotaWindow.DisplayUsed(longWindow.used), true);
            if (longWindow == null) return DrawSingleIcon(QuotaWindow.DisplayUsed(shortWindow.used), true);
            return DrawIcon(QuotaWindow.DisplayUsed(shortWindow.used), QuotaWindow.DisplayUsed(longWindow.used), true);
        }

        internal static Bitmap RenderMultiTrayGauge(ConnectionReply reply, int size)
        {
            WindowInfo shortWindow, longWindow;
            MultiWindows(reply, out shortWindow, out longWindow);
            if (shortWindow == null && longWindow == null) return RenderTrayGauge(size, 0, 0, false);
            if (shortWindow == null) return RenderSingleGauge(size, QuotaWindow.DisplayUsed(longWindow.used), true, true);
            if (longWindow == null) return RenderSingleGauge(size, QuotaWindow.DisplayUsed(shortWindow.used), true, true);
            return RenderTrayGauge(size, QuotaWindow.DisplayUsed(shortWindow.used), QuotaWindow.DisplayUsed(longWindow.used), true);
        }

        internal static string MultiCountdown(double reset)
        {
            if (double.IsNaN(reset) || double.IsInfinity(reset) || reset <= 0 || reset > 253402300799) return "?";
            string text = CountdownTray(reset);
            return L.Lang == "zh" ? text.Replace("后恢复", "后").Replace(" ", "") : text;
        }

        internal static string MultiTooltipCountdown(double reset)
        {
            if (double.IsNaN(reset) || double.IsInfinity(reset) || reset <= 0 || reset > 253402300799) return L.Lang == "zh" ? L.T("恢复时间未知") : CountdownTray(0);
            return L.Lang == "zh" ? Countdown(reset) : CountdownTray(reset);
        }

        private void LoadMultiView(ConnectionReply reply, string label, string error, DateTime success)
        {
            WindowInfo shortWindow, longWindow;
            MultiWindows(reply, out shortWindow, out longWindow);
            hasData = shortWindow != null || longWindow != null;
            primaryUsed = shortWindow == null ? -1 : shortWindow.used;
            primaryReset = shortWindow == null ? 0 : shortWindow.resetsAt;
            secondaryUsed = longWindow == null ? -1 : longWindow.used;
            secondaryReset = longWindow == null ? 0 : longWindow.resetsAt;
            secondaryDays = longWindow == null ? 7 : Math.Max(1, (int)Math.Round(longWindow.windowMins / 1440));
            plan = reply == null || reply.identity == null ? "" : reply.identity.plan;
            viewAccountLabel = label ?? "";
            viewAccountSlot = reply == null ? null : reply.slot;
            lastError = error ?? "";
            lastOkAt = success;
        }

        internal void ShowMultiDetail(ConnectionReply reply, string label, string error, DateTime lastSuccess)
        {
            LoadMultiView(reply, label, error, lastSuccess);
            ShowDetail();
        }

        internal void SyncMultiDesktop(ConnectionReply reply)
        {
            WindowInfo shortWindow, longWindow;
            MultiWindows(reply, out shortWindow, out longWindow);
            UpdateDesktopIcon(shortWindow == null ? -1 : shortWindow.used, longWindow == null ? -1 : longWindow.used);
        }

        internal void ShowMultiToast(string slot, string label, string body, string soundFile, string emailMask, bool compact = false)
        {
            accountToasts.Enqueue(slot, label, body, soundFile, emailMask, compact);
        }

        private UiQuotaTip quotaTip;
        private UiTrayMenu trayMenu;
        // 右键菜单优先：菜单开着的时候不弹悬停提示，否则提示窗口会把菜单顶掉。
        internal bool QuotaTipBlockedByMenu()
        {
            return trayMenu != null && trayMenu.PopupVisible;
        }
        internal static QuotaTipLine[] BuildQuotaTipLines(ConnectionReply reply, string error, DateTime lastSuccess)
        {
            var rows = new System.Collections.Generic.List<QuotaTipLine>();
            if (reply != null)
            {
                bool longExhausted = false;
                foreach (WindowInfo w in new WindowInfo[] { reply.primary, reply.secondary })
                    if (w != null && QuotaWindow.IsLong(w.windowMins) && QuotaWindow.Remaining(w.used) <= 0)
                        longExhausted = true;
                foreach (WindowInfo w in new WindowInfo[] { reply.primary, reply.secondary })
                {
                    if (w == null) continue;
                    bool isLong = QuotaWindow.IsLong(w.windowMins);
                    rows.Add(new QuotaTipLine
                    {
                        Label = isLong ? L.F("{0} 天", QuotaWindow.Days(w.windowMins)) : L.T("5 小时"),
                        Remaining = QuotaWindow.Remaining(w.used),
                        Detail = MultiTooltipCountdown(w.resetsAt),
                        Superseded = !isLong && longExhausted
                    });
                }
            }
            if (rows.Count == 0 && error != null)
                rows.Add(new QuotaTipLine { Label = L.T("更新失败"), Remaining = -1,
                    Detail = L.T("最后成功") + " " + (lastSuccess == DateTime.MinValue ? L.T("暂无") : lastSuccess.ToString("MM-dd HH:mm")) });
            return rows.ToArray();
        }
        internal bool ShowQuotaTip(NotifyIcon icon, string header, ConnectionReply reply, string error, DateTime lastSuccess)
        {
            if (QuotaTipBlockedByMenu()) return true;
            try
            {
                QuotaTipLine[] rows = BuildQuotaTipLines(reply, error, lastSuccess);
                if (rows.Length == 0) { HideQuotaTip(); return true; }
                if (quotaTip == null || quotaTip.IsDisposed) quotaTip = new UiQuotaTip();
                quotaTip.Present(icon, header, rows);
                return true;
            }
            catch
            {
                // Fall back to the native tooltip rather than losing hover info.
                if (quotaTip != null) { try { quotaTip.Dispose(); } catch { } quotaTip = null; }
                return false;
            }
        }
        internal void HideQuotaTip() { if (quotaTip != null && !quotaTip.IsDisposed) quotaTip.Dismiss(); }
        internal bool RefreshQuotaTip(NotifyIcon icon, string header, ConnectionReply reply, string error, DateTime lastSuccess)
        {
            if (quotaTip == null || quotaTip.IsDisposed || !quotaTip.PrepareRefresh(icon)) return true;
            return ShowQuotaTip(icon, header, reply, error, lastSuccess);
        }

        internal void EnsureMultiShortcut(ConnectionReply reply)
        {
            if (EnsureDesktopShortcut(true))
            {
                desktopIconKey = "";
                SyncMultiDesktop(reply);
                ShowBalloon(L.T("已创建"), L.T("桌面快捷方式已就位，图标会跟着额度变化。"));
            }
        }

        // ---------- 找 Codex ----------
        // 已经认过的路径还在就直接用；配置里没有就先自动找一个，找不到再等界面提示
        private void PrepareCodexPath()
        {
            string found = settings.DetectCodexOnce(root, CodexFinder.Find);
            if (!string.IsNullOrEmpty(found)) Log("自动识别到 Codex：" + found);
        }

        private void ShowLocateDialog() { ShowLocateDialog(false); }

        private void ShowLocateDialog(bool fromConnections)
        {
            if (!fromConnections && accountForm != null && !accountForm.IsDisposed)
            {
                UiTheme.Present(accountForm);
                ShowBalloon(L.T("账号连接"), L.T("请先关闭账号连接窗口。"));
                return;
            }
            using (CodexLocateForm f = new CodexLocateForm(settings.codexPath))
            {
                if (f.ShowDialog(fromConnections ? accountForm : null) != DialogResult.OK) return;
                settings.codexPath = f.SelectedPath;
                settings.codexLookupAttempted = true;
                settings.Save(root);
                nodeExe = FindNode(settings.codexPath);
                if (fromConnections && accountForm != null) accountForm.SetCodexPath(settings.codexPath, nodeExe);
                Log("手动识别 Codex：" + settings.codexPath);
                RestartMultiAccounts();
                refreshNow.Set();
            }
        }

        // ---------- 语言 ----------
        private void ApplyLanguageSetting()
        {
            L.Lang = L.Guess(settings.language);
        }

        private void SetLanguage(string v)
        {
            settings.language = v;
            settings.Save(root);
            ApplyLanguageSetting();
            if (trayMenu != null)
            {
                Font oldMenuFont = trayMenu.Font;
                trayMenu.Font = UiTheme.UiFont(9.5f);
                if (oldMenuFont != null) oldMenuFont.Dispose();
            }
            ApplyLanguage();
            Log("语言 -> " + v);
        }

        // 就地换掉所有菜单文字，不重建菜单
        private void ApplyLanguage()
        {
            if (menuRefresh == null) return;
            menuRefresh.Text = L.T("刷新");
            menuDetail.Text = L.T("额度详情");
            menuNotify.Text = L.T("额度提醒");
            menuLevels[0].Text = L.T("关闭提醒");
            menuLevels[1].Text = L.T("低档");
            menuLevels[2].Text = L.T("中档");
            menuLevels[3].Text = L.T("高档");
            // 悬停在某一档上才说明它什么时候弹，菜单本身保持干净
            menuLevels[0].ToolTipText = L.T("不弹任何提醒");
            menuLevels[1].ToolTipText = L.T("剩 25% 和 10% 时各提醒一次") + "\r\n" + L.T("7 天额度固定剩 25%、10%");
            menuLevels[2].ToolTipText = L.T("剩 75%、25%、10% 时各提醒一次") + "\r\n" + L.T("7 天额度固定剩 25%、10%");
            menuLevels[3].ToolTipText = L.T("剩 75%、50%、25%、10% 时各提醒一次") + "\r\n" + L.T("7 天额度固定剩 25%、10%");
            menuAutoStart.Text = L.T("开机自启动");
            menuShortcut.Text = L.T("创建桌面快捷方式");
            menuExit.Text = L.T("退出");
            menuLang.Text = L.T("语言");
            menuAccounts.Text = L.T("账号");
            menuDisplay.Text = L.T("设置");
            menuLangItems[0].Text = L.T("自动");
            menuLangItems[1].Text = "中文";
            menuLangItems[2].Text = "日本語";
            menuLangItems[3].Text = "English";
            UpdateAutoStartItem(menuAutoStart);
            if (multiMode && multiAccounts != null) multiAccounts.ApplyLanguage();
            else UpdateUi();
        }

        // ---------- 额度提醒 ----------
        private bool ApplyGlobalNotifyLevel(int level)
        {
            if (!settings.ApplyGlobalLevel(root, level)) return false;
            accountToasts.Clear();
            if (multiAccounts != null)
                foreach (string slot in new[] { "a", "b", "c", "d", "e" }) multiAccounts.ResetReminder(slot);
            notifyTiered = null;
            notifyFixed = null;
            Log("全局额度提醒档位 -> " + settings.Key());
            return true;
        }

        private void EnsureNotifiers(bool singleWindow)
        {
            int[] tier = singleWindow ? NotifyRules.LongTier(settings.notifyLevel) : NotifyRules.Tier(settings.notifyLevel);
            string tag = singleWindow ? "long" : "short";
            if (notifyTiered == null || notifyTiered.name != tag || !SameThresholds(notifyTiered.thresholds, tier))
            {
                notifyTiered = new NotifyWindow();
                notifyTiered.name = tag;
                notifyTiered.thresholds = tier;
            }
            int[] longTier = NotifyRules.LongTier(settings.notifyLevel);
            if (notifyFixed == null || !SameThresholds(notifyFixed.thresholds, longTier))
            {
                notifyFixed = new NotifyWindow();
                notifyFixed.name = "long";
                notifyFixed.thresholds = longTier;
            }
        }

        private static bool SameThresholds(int[] a, int[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i]) return false;
            }
            return true;
        }

        // 一次采样后统一判定。档位只管"阶梯窗口"：
        //   Plus —— 5 小时额度按档位；7 天额度固定两刀（剩余 25%、剩余 10%）
        //   Pro  —— 只有 7 天额度，按档位
        private void EvaluateNotify()
        {
            if (!hasData) return;
            if (secondaryUsed >= 0 && secondaryDays >= 1)
            {
                if (dailyPace == null) dailyPace = DailyPaceTracker.Load(root);
                DailyPaceDecision pace = dailyPace.Evaluate(null, settings.dailyPaceDefault,
                    secondaryUsed, secondaryReset, DateTimeOffset.UtcNow);
                if (pace.Triggered)
                    accountToasts.Enqueue(null, L.T("额度显示器"), NotifyRules.DailyPaceBody(), NoSound, null, true);
            }
            if (settings.notifyLevel < 0) return;
            bool singleWindow = primaryUsed < 0;
            EnsureNotifiers(singleWindow);

            if (singleWindow)
            {
                if (secondaryUsed < 0) return;
                int t = notifyTiered.Fire(secondaryUsed, secondaryReset);
                if (notifyTiered.Recovered) NotifyRecovered(secondaryDays);
                else if (notifyTiered.Exhausted) NotifyExhausted(L.F("{0} 天", secondaryDays), "notify-7d-empty.wav");
                else if (t > 0) NotifyQuota(L.F("{0} 天", secondaryDays), secondaryUsed, secondaryReset, t);
                return;
            }

            if (primaryUsed >= 0)
            {
                int t = notifyTiered.Fire(primaryUsed, primaryReset);
                if (notifyTiered.Recovered) NotifyRecovered(0);
                else if (notifyTiered.Exhausted) NotifyExhausted(L.T("5 小时"), "notify-5h-empty.wav");
                else if (t > 0) NotifyQuota(L.T("5 小时"), primaryUsed, primaryReset, t);
            }
            if (secondaryUsed >= 0)
            {
                int t = notifyFixed.Fire(secondaryUsed, secondaryReset);
                if (notifyFixed.Recovered) NotifyRecovered(secondaryDays);
                else if (notifyFixed.Exhausted) NotifyExhausted(L.F("{0} 天", secondaryDays), "notify-7d-empty.wav");
                else if (t > 0) NotifyQuota(L.F("{0} 天", secondaryDays), secondaryUsed, secondaryReset, t);
            }
        }

        private void NotifyQuota(string windowName, double used, double reset, int threshold)
        {
            int remain = 100 - (int)Math.Round(used);
            if (remain < 0) remain = 0;
            if (remain > 100) remain = 100;
            bool lastCall = threshold >= 90;
            string body = L.F(lastCall ? "{0}额度只剩 {1}%｜{2}" : "{0}额度还剩 {1}%｜{2}",
                windowName, remain, Countdown(reset));
            ShowToast(L.T("额度提醒"), body, "notify.wav");
            Log("额度提醒 -> " + body);
        }

        // 用光了：文案和提示音都单独一套
        private void NotifyExhausted(string windowName, string soundFile)
        {
            string body = L.F("{0}额度已耗尽", windowName);
            ShowToast(L.T("额度提醒"), body, soundFile);
            Log("额度耗尽 -> " + body);
        }

        private void NotifyRecovered(int days)
        {
            if (!NotifyRules.CanShowRecovery(plan, days)) return;
            accountToasts.Enqueue(null, L.T("额度显示器"), NotifyRules.RecoveryBody(days), RecoverySound, null, true);
        }

        internal const string NoSound = "";

        // 自绘弹窗，按设置决定是否在滑入时播放提示音
        private void ShowToast(string title, string body, string soundFile, string slot = null, string emailMask = null, bool compact = false)
        {
            try
            {
                if (toast != null)
                {
                    try { toast.Close(); toast.Dispose(); } catch { }
                    toast = null;
                }
                toast = new ToastForm(title, body, slot, emailMask, compact);
                if (soundFile != NoSound) AttachToastSound(toast, root, soundFile, Log, settings.notifySound, settings.notifyVolume);
                toast.Show();
            }
            catch (Exception ex)
            {
                Log("弹窗失败：" + ex.Message);
            }
        }

        internal static void StopToastSound()
        {
            ToastSound.Stop();
            if (fallbackToastSound == null) return;
            fallbackToastSound.Stop();
            fallbackToastSound = null;
        }

        internal static void AttachToastSound(ToastForm form, string dir, string soundFile, Action<string> report,
            bool enabled = true, int volume = AppSettings.DefaultNotifyVolume)
        {
            if (enabled && volume > 0 && soundFile != NoSound) form.SynchronizeSlideIn = PrepareToastSound(dir, soundFile, volume, report);
        }

        internal static void PlayNotifySample(string dir, int volume, Action<string> report)
        {
            if (volume == 0) { StopToastSound(); return; }
            Action<Action> play = PrepareToastSound(dir, "notify.wav", volume, report);
            if (play != null) play(null);
        }

        private static Action<Action> PrepareToastSound(string dir, string soundFile, int volume, Action<string> report)
        {
            try
            {
                System.Media.SoundPlayer player = LoadNotifySound(dir, soundFile);
                if (player == null) return null;
                ToastSound.Format format;
                byte[] samples = CopyToastPcm(player, out format);
                if (volume < 100 && samples == null)
                {
                    // Use the same built-in alert if a custom WAV cannot be attenuated as PCM.
                    player = LoadNotifySound(dir, soundFile, true);
                    if (player != null) samples = CopyToastPcm(player, out format);
                    if (samples == null) throw new InvalidOperationException("No adjustable PCM alert is available.");
                }
                if (samples != null) ToastSound.ScalePcm(samples, format, volume);
                return delegate(Action slideIn)
                {
                    try
                    {
                        StopToastSound();
                        if (samples != null && ToastSound.TryPlay(format, samples, slideIn)) return;
                        fallbackToastSound = volume == 100 ? player : new System.Media.SoundPlayer(ToastSound.PcmWave(format, samples));
                        fallbackToastSound.Play();
                    }
                    catch (Exception ex) { report("提示音失败：" + ex.Message); }
                    if (slideIn != null) slideIn();
                };
            }
            catch (Exception ex)
            {
                report("提示音失败：" + ex.Message);
                return null;
            }
        }

        private static byte[] CopyToastPcm(System.Media.SoundPlayer player, out ToastSound.Format format)
        {
            player.Stream.Position = 0;
            using (var source = new MemoryStream())
            {
                player.Stream.CopyTo(source);
                source.Position = 0;
                return ToastSound.ReadPcm(source, out format);
            }
        }

        internal const string RecoverySound = "notify-restored.wav";
        internal static readonly string[] NotifySoundFiles = new string[] { "notify.wav", "notify-5h-empty.wav", "notify-7d-empty.wav", RecoverySound };
        internal static System.Media.SoundPlayer LoadNotifySound(string dir,string soundFile,bool embeddedOnly=false)
        {
            string requested=String.IsNullOrEmpty(soundFile)?"notify.wav":soundFile;
            string path=Path.Combine(dir,requested);
            string key=path;
            Stream stream=null;
            bool fromFile = !embeddedOnly && File.Exists(path);
            if (!fromFile) key = "resource:" + requested;
            if (players == null) players = new System.Collections.Generic.Dictionary<string,System.Media.SoundPlayer>();
            System.Media.SoundPlayer player;
            if (players.TryGetValue(key, out player)) return player;
            stream = fromFile ? new MemoryStream(File.ReadAllBytes(path), false) :
                Assembly.GetExecutingAssembly().GetManifestResourceStream(requested);
            if (stream == null)
                return requested == "notify.wav" ? null : LoadNotifySound(dir, "notify.wav", embeddedOnly);
            player = new System.Media.SoundPlayer(stream);
            player.Load(); players[key]=player; return player;
        }

        // ---------- 绿色版：数据目录 / 内嵌 js / 找 node ----------

        // 优先用 exe 所在目录（便携）；写不进去（比如放在 Program Files）就退到 %APPDATA%
        private static string ResolveDataDir()
        {
            try
            {
                string exeDir = Path.GetDirectoryName(Application.ExecutablePath);
                if (!string.IsNullOrEmpty(exeDir) && Directory.Exists(exeDir))
                {
                    string probe = Path.Combine(exeDir, ".write-test");
                    File.WriteAllText(probe, "1");
                    File.Delete(probe);
                    return exeDir;
                }
            }
            catch { }
            string appData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "CodexQuotaTray");
            try { Directory.CreateDirectory(appData); } catch { }
            return appData;
        }

        // quota.js 编译时用 /resource 打进 exe 里了。文件不在就解出来，
        // 这样别人只需要一个 exe，不用再带一个 .js。
        private void EnsureQuotaJs()
        {
            try
            {
                if (File.Exists(quotaJs)) return;
                var asm = System.Reflection.Assembly.GetExecutingAssembly();
                using (var s = asm.GetManifestResourceStream("quota.js"))
                {
                    if (s == null) return;
                    using (var fs = File.Create(quotaJs))
                    {
                        s.CopyTo(fs);
                    }
                }
            }
            catch (Exception ex)
            {
                try { Log("释放内嵌 quota.js 失败：" + ex.Message); } catch { }
            }
        }

        // 找 node：便携目录 → 系统装的 → Codex 自带的运行时（装了 Codex 就一定有）
        internal static string FindNode(string codexPath)
        {
            string bundled = CodexFinder.BundledNode(codexPath);
            if (bundled != null) return bundled;
            try
            {
                string exeDir = Path.GetDirectoryName(Application.ExecutablePath);
                string local = Path.Combine(exeDir, "node.exe");
                if (File.Exists(local)) return local;
            }
            catch { }

            string p = @"C:\Program Files\nodejs\node.exe";
            if (File.Exists(p)) return p;

            // Codex 桌面版自带的 node 运行时（多个版本时取最新那个）
            try
            {
                string rt = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    @"OpenAI\Codex\runtimes\cua_node");
                if (Directory.Exists(rt))
                {
                    string best = null;
                    DateTime bestTime = DateTime.MinValue;
                    foreach (string d in Directory.GetDirectories(rt))
                    {
                        string cand = Path.Combine(d, @"bin\node.exe");
                        if (!File.Exists(cand)) continue;
                        DateTime t = File.GetLastWriteTimeUtc(cand);
                        if (t > bestTime) { bestTime = t; best = cand; }
                    }
                    if (best != null) return best;
                }
            }
            catch { }

            string alt = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                @".cache\codex-runtimes\codex-primary-runtime\dependencies\node\bin\node.exe");
            if (File.Exists(alt)) return alt;
            return "node.exe";
        }

        // ---------- 后台线程：定期查询 ----------
        private void WorkerLoop()
        {
            // 所有真实读取统一经过已确认绑定的账号控制器。
            // 未绑定或用户取消时，不能退回隐式读取Codex当前登录。
            stopWorker.WaitOne();
        }

        private QuotaResult Query()
        {
            var r = new QuotaResult();
            try
            {
                if (!File.Exists(quotaJs))
                {
                    r.error = L.T("找不到 quota.js");
                    return r;
                }
                var psi = new ProcessStartInfo(nodeExe, "\"" + quotaJs + "\"");
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.StandardOutputEncoding = Encoding.UTF8;
                psi.StandardErrorEncoding = Encoding.UTF8;
                // 认到的 codex.exe 直接告诉 quota.js，省得它自己再猜一次
                if (!string.IsNullOrEmpty(settings.codexPath) && File.Exists(settings.codexPath))
                {
                    psi.EnvironmentVariables["CODEX_CLI_PATH"] = settings.codexPath;
                }

                using (var p = Process.Start(psi))
                {
                    string outp = p.StandardOutput.ReadToEnd();
                    p.StandardError.ReadToEnd();
                    if (!p.WaitForExit(20000))
                    {
                        try { p.Kill(); } catch { }
                        r.error = L.T("查询超时");
                        return r;
                    }
                    string line = outp.Trim();
                    if (line.Length == 0)
                    {
                        r.error = L.T("查询无输出");
                        return r;
                    }
                    var ser = new JavaScriptSerializer();
                    var parsed = ser.Deserialize<QuotaResult>(line);
                    if (parsed == null)
                    {
                        r.error = L.T("返回格式异常");
                        return r;
                    }
                    return parsed;
                }
            }
            catch (Exception ex)
            {
                r.error = ex.Message;
                return r;
            }
        }

        // ---------- UI 线程：取结果并刷新显示 ----------
        private void PumpResults()
        {
            if (multiMode)
            {
                lock (gate) { pending = null; hasPending = false; }
                return;
            }
            QuotaResult r = null;
            lock (gate)
            {
                if (hasPending)
                {
                    r = pending;
                    pending = null;
                    hasPending = false;
                }
            }
            if (r == null) return;

            if (!r.ok)
            {
                lastError = string.IsNullOrEmpty(r.error) ? L.T("查询失败，稍后重试") : TranslateQuotaError(r.error);
                // 掉线了（换机器、重装 Codex、路径变了）就再给一次识别机会
                if (!locateShown && lastError.IndexOf("codex.exe", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    needLocate = true;
                }
                Log("查询失败：" + lastError);
                if (!hasData) UpdateUi();
                return;
            }

            lastError = "";
            hasData = true;
            plan = r.plan == null ? "" : r.plan;

            if (r.primary != null && r.primary.used >= 0)
            {
                // 只有"确实是个短窗口"才当 5 小时额度用。
                // PRO 100/200/500 官方说明没有 5 小时限额；万一它的 primary 其实是周额度
                // （窗口长度超过 1 天），这里要能识别出来，否则上下两段会显示同一个数字。
                if (QuotaWindow.IsLong(r.primary.windowMins))
                {
                    primaryUsed = -1;
                }
                else
                {
                    primaryUsed = r.primary.used;
                    primaryReset = r.primary.resetsAt;
                }
            }

            // 7 天额度变化慢，每 5 分钟才更新一次显示
            bool needSecondary = secondaryShownAt == DateTime.MinValue ||
                (DateTime.UtcNow - secondaryShownAt).TotalSeconds >= SecondaryEverySec;
            if (needSecondary && r.secondary != null && r.secondary.used >= 0)
            {
                secondaryUsed = r.secondary.used;
                secondaryReset = r.secondary.resetsAt;
                if (r.secondary.windowMins > 0)
                    secondaryDays = (int)Math.Round(r.secondary.windowMins / 1440.0);
                secondaryShownAt = DateTime.UtcNow;
            }

            // 兜底：有些返回结构会把唯一的周额度放在 primary、secondary 留空。
            // 不补这一手，Pro 会显示成"不限制 + 暂无数据"，等于什么都看不见。
            if (secondaryUsed < 0 && r.primary != null && r.primary.used >= 0 && QuotaWindow.IsLong(r.primary.windowMins))
            {
                secondaryUsed = r.primary.used;
                secondaryReset = r.primary.resetsAt;
                secondaryDays = (int)Math.Round(r.primary.windowMins / 1440.0);
                secondaryShownAt = DateTime.UtcNow;
            }

            lastOkAt = DateTime.Now;

            // 记下两个窗口的实际长度：PRO 系列本来就没有 5 小时档，
            // 万一哪天返回结构变了，翻日志能直接看出来。
            string winKey = "plan=" + plan
                + " primary=" + (r.primary == null ? "null" : r.primary.windowMins + "min")
                + " secondary=" + (r.secondary == null ? "null" : r.secondary.windowMins + "min");
            if (winKey != lastWindows)
            {
                lastWindows = winKey;
                Log("额度窗口：" + winKey);
            }

            string key = ((int)primaryUsed) + "/" + ((int)secondaryUsed);
            if (key != lastLogged)
            {
                Log("额度更新：5小时=" + ((int)primaryUsed) + "% 7天=" + ((int)secondaryUsed) + "%");
                lastLogged = key;
            }

            UpdateUi();
            UpdateDesktopIcon(primaryUsed, secondaryUsed);
            EvaluateNotify();
        }

        // ---------- 让桌面快捷方式的图标跟着托盘一起动 ----------
        // 原理：把当前百分比渲染成新的 .ico，改写快捷方式的 IconLocation 指过去，
        // 再让 Shell 刷新那一个图标。两个要点：
        //   ① 每次用新文件名（Explorer 按路径缓存图标，同路径覆盖内容它经常不认）；
        //      文件名里带本次运行时刻，避免重启后撞上旧缓存；
        //   ② 只有百分比真的变了才写，避免白折腾和桌面闪。
        private void UpdateDesktopIcon(double pUsed, double sUsed)
        {
            if (noDesktopTouch || accountDiscoveryPending) return;
            try
            {
                // 关闭开关：程序目录里放一个 disable-desktop-icon.txt 就不动桌面图标
                if (File.Exists(Path.Combine(root, "disable-desktop-icon.txt"))) return;
                string lnkPath = FindDesktopShortcut(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), Application.ExecutablePath);
                if (lnkPath == null) return;
                bool hasQuota = pUsed >= 0 || sUsed >= 0;
                bool single = hasQuota && (pUsed < 0 || sUsed < 0);
                int p = pUsed < 0 ? -1 : QuotaWindow.DisplayUsed(pUsed);
                int s = sUsed < 0 ? -1 : QuotaWindow.DisplayUsed(sUsed);
                string key = "v2:" + (single ? "S" : "D") + p + "/" + s;
                if (key == desktopIconKey && string.Equals(lnkPath, desktopIconLink, StringComparison.OrdinalIgnoreCase)) return;

                desktopIconSeq++;
                string icoPath = Path.Combine(root,
                    "desktop-icon-" + desktopIconRunTag + "-" + desktopIconSeq + ".ico");
                WriteIco(icoPath, Math.Max(0, p), single && s < 0 ? p : Math.Max(0, s), single, hasQuota);

                Type shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType == null) return;

                object sh = Activator.CreateInstance(shellType);
                try
                {
                    object lnk = shellType.InvokeMember("CreateShortcut",
                        BindingFlags.InvokeMethod, null, sh, new object[] { lnkPath });
                    Type lt = lnk.GetType();
                    lt.InvokeMember("IconLocation", BindingFlags.SetProperty,
                        null, lnk, new object[] { icoPath + ",0" });
                    lt.InvokeMember("Save", BindingFlags.InvokeMethod, null, lnk, null);
                    Marshal.ReleaseComObject(lnk);
                }
                finally
                {
                    Marshal.ReleaseComObject(sh);
                }

                desktopIconKey = key;
                desktopIconLink = lnkPath;
                SHChangeNotify(0x2000, 0x0005, lnkPath, IntPtr.Zero);
                Log("桌面图标已同步：" + key + "（" + Path.GetFileName(icoPath) + "）");
                SaveDesktopIconState(key, icoPath);
                CleanupOldDesktopIcons();
            }
            catch (Exception ex)
            {
                Log("桌面图标同步失败：" + ex.Message);
            }
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern void SHChangeNotify(int eventId, uint flags, string item1, IntPtr item2);

        internal static string FindDesktopShortcut(string desktop, string executable)
        {
            foreach (var shortcut in ReadDesktopShortcuts(desktop))
                if (String.Equals(shortcut.Value, executable, StringComparison.OrdinalIgnoreCase)) return shortcut.Key;
            return null;
        }

        internal static IEnumerable<KeyValuePair<string, string>> ReadDesktopShortcuts(string desktop)
        {
            Type shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null || string.IsNullOrEmpty(desktop)) yield break;
            object shell = Activator.CreateInstance(shellType);
            try
            {
                // The program language can change while an existing shortcut keeps its original name.
                foreach (string name in new[] { "额度显示器.lnk", "Codex Quota.lnk", "クォータトレイ.lnk" })
                {
                    string path = Path.Combine(desktop, name);
                    if (!File.Exists(path)) continue;
                    object link = null;
                    string target;
                    try
                    {
                        link = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { path });
                        target = (string)link.GetType().InvokeMember("TargetPath", BindingFlags.GetProperty, null, link, null);
                    }
                    catch (COMException) { continue; }
                    catch (TargetInvocationException) { continue; }
                    finally { if (link != null) Marshal.ReleaseComObject(link); }
                    if (!String.IsNullOrEmpty(target)) yield return new KeyValuePair<string, string>(path, target);
                }
            }
            finally { Marshal.ReleaseComObject(shell); }
        }

        // 记下这次用的"数值 key + ico 路径"，供下次启动判断要不要重画
        private void SaveDesktopIconState(string key, string icoPath)
        {
            try
            {
                File.WriteAllText(Path.Combine(root, "desktop-icon-state.txt"),
                    key + "\n" + icoPath + "\n");
            }
            catch (Exception ex)
            {
                Log("保存桌面图标状态失败：" + ex.Message);
            }
        }

        // 启动时读回上次状态。三个条件都满足才认定"可以直接用"：
        //   ① 上次的 ico 文件还在；② 快捷方式确实还指着它；③ 上次的数值 key 有效。
        // 满足就把 desktopIconKey 预填成上次的值，于是第一次数据回来若没变就直接 return，
        // 不写文件、不改 lnk、不通知 Shell —— 桌面完全不动。
        private void LoadDesktopIconState()
        {
            try
            {
                string statePath = Path.Combine(root, "desktop-icon-state.txt");
                if (!File.Exists(statePath)) return;

                string[] parts = File.ReadAllText(statePath).Split('\n');
                if (parts.Length < 2) return;
                string key = parts[0].Trim();
                string ico = parts[1].Trim();
                if (!key.StartsWith("v2:", StringComparison.Ordinal) || ico.Length == 0) return;
                if (!File.Exists(ico)) return;

                string lnkPath = FindDesktopShortcut(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), Application.ExecutablePath);
                if (lnkPath == null) return;

                Type shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType == null) return;
                object sh = Activator.CreateInstance(shellType);
                try
                {
                    object lnk = shellType.InvokeMember("CreateShortcut",
                        BindingFlags.InvokeMethod, null, sh, new object[] { lnkPath });
                    Type lt = lnk.GetType();
                    string loc = (string)lt.InvokeMember("IconLocation",
                        BindingFlags.GetProperty, null, lnk, null);
                    Marshal.ReleaseComObject(lnk);

                    if (loc == null) return;
                    string locPath = loc.Split(',')[0].Trim().Trim('"');
                    if (!string.Equals(locPath, ico, StringComparison.OrdinalIgnoreCase)) return;
                }
                finally
                {
                    Marshal.ReleaseComObject(sh);
                }

                desktopIconKey = key;
                desktopIconLink = lnkPath;
                Log("桌面图标沿用上次状态：" + key + "（不重绘）");
            }
            catch (Exception ex)
            {
                Log("读取桌面图标状态失败：" + ex.Message);
            }
        }

        // 绿色版开箱即用：确保桌面有指向本 exe 的快捷方式。
        // force=false 时已存在就不动；force=true 用于右键菜单"重建"。
        private bool EnsureDesktopShortcut(bool force)
        {
            try
            {
                string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                string lnkPath = FindDesktopShortcut(desktop, Application.ExecutablePath) ?? Path.Combine(desktop, L.T("额度显示器.lnk"));
                if (File.Exists(lnkPath) && !force) return true;

                Type shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType == null) return false;

                object sh = Activator.CreateInstance(shellType);
                try
                {
                    object lnk = shellType.InvokeMember("CreateShortcut",
                        BindingFlags.InvokeMethod, null, sh, new object[] { lnkPath });
                    Type lt = lnk.GetType();
                    lt.InvokeMember("TargetPath", BindingFlags.SetProperty, null, lnk,
                        new object[] { Application.ExecutablePath });
                    lt.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, lnk,
                        new object[] { root });
                    lt.InvokeMember("IconLocation", BindingFlags.SetProperty, null, lnk,
                        new object[] { Application.ExecutablePath + ",0" });
                    lt.InvokeMember("Description", BindingFlags.SetProperty, null, lnk,
                        new object[] { L.T("Codex 额度显示器，右下角托盘") });
                    lt.InvokeMember("Save", BindingFlags.InvokeMethod, null, lnk, null);
                    Marshal.ReleaseComObject(lnk);
                }
                finally
                {
                    Marshal.ReleaseComObject(sh);
                }

                Log("桌面快捷方式已创建：" + lnkPath);
                return true;
            }
            catch (Exception ex)
            {
                Log("创建桌面快捷方式失败：" + ex.Message);
                return false;
            }
        }

        private void ShowBalloon(string title, string text)
        {
            if (multiMode && multiAccounts != null) { multiAccounts.ShowBalloon(title, text); return; }
            try
            {
                tray.ShowBalloonTip(5000, title, text, ToolTipIcon.Info);
            }
            catch { }
        }

        // 只保留最近 3 个 .ico（按修改时间）
        private void CleanupOldDesktopIcons()
        {
            try
            {
                string currentIco = "desktop-icon-" + desktopIconRunTag + "-" + desktopIconSeq + ".ico";
                var list = new System.Collections.Generic.List<FileInfo>();
                foreach (string f in Directory.GetFiles(root, "desktop-icon-*.ico"))
                {
                    list.Add(new FileInfo(f));
                }
                list.Sort(delegate(FileInfo a, FileInfo b)
                {
                    return b.LastWriteTimeUtc.CompareTo(a.LastWriteTimeUtc);
                });
                for (int i = 3; i < list.Count; i++)
                {
                    if (string.Equals(list[i].Name, currentIco, StringComparison.OrdinalIgnoreCase)) continue;
                    try { list[i].Delete(); } catch { }
                }
            }
            catch { }
        }

        private void UpdateUi()
        {
            if (hasData)
            {
                // 悬停文案报「剩余」而不是「已用」——直接告诉他还剩多少，不用自己拿 100 去减。
                // 取 100 - round(used)，保证是个整数、且与已用正好互补。
                //
                // 注意 used = -1 表示「这一档根本不存在」：PRO 100/200/500 官方明确
                // 「没有 5 小时使用限额」，只有每周额度。这时上段不应该算出 101%，
                // 而是标成「不限制」，图标上段画成全绿（= 剩余 100%，用不完）。
                // Pro 只有周额度：没有短窗口 → 单环、单行悬停
                if (primaryUsed < 0 && secondaryUsed >= 0)
                {
                    tray.Text = ClampTray(L.F("{0} 天 剩余 {1}%｜{2}", secondaryDays,
                        100 - (int)Math.Round(secondaryUsed), CountdownTray(secondaryReset)));
                    SetIcon(DrawSingleIcon(Math.Max(0.0, secondaryUsed), true));
                }
                else
                {
                    string line1, line2;
                    if (primaryUsed < 0)
                    {
                        line1 = L.T("5 小时 不限制");
                    }
                    else
                    {
                        line1 = L.F("5 小时 剩余 {0}%｜{1}",
                            100 - (int)Math.Round(primaryUsed), CountdownTray(primaryReset));
                    }
                    if (secondaryUsed < 0)
                    {
                        line2 = L.F("{0} 天 暂无数据", secondaryDays);
                    }
                    else
                    {
                        line2 = L.F("{0} 天 剩余 {1}%｜{2}", secondaryDays,
                            100 - (int)Math.Round(secondaryUsed), CountdownTray(secondaryReset));
                    }
                    tray.Text = ClampTray(line1 + "\n" + line2);

                    // 传 0（而不是 -1）给绘制函数：那一档不存在时按"剩余 100%"画成整圈绿
                    SetIcon(DrawIcon(Math.Max(0.0, primaryUsed), Math.Max(0.0, secondaryUsed), true));
                }
            }
            else
            {
                tray.Text = ClampTray(L.T("额度显示器") + " | "
                    + (string.IsNullOrEmpty(lastError) ? L.T("正在读取额度…") : lastError)
                    + "\n" + L.T("（右键可退出）"));
                SetIcon(DrawIcon(0, 0, false));
            }

            if (!multiMode && !hasData)
                tray.Text = ClampTray(L.T(AccountStatusKey()) + (settings.HasCodex ? "" : "\n" + L.T("未识别到 Codex")));

            // 状态快照（方便排查）
            try
            {
                var sb = new StringBuilder();
                sb.Append("{");
                sb.Append("\"hasData\":").Append(hasData ? "true" : "false").Append(",");
                sb.Append("\"primaryUsed\":").Append((int)Math.Round(primaryUsed)).Append(",");
                sb.Append("\"secondaryUsed\":").Append((int)Math.Round(secondaryUsed)).Append(",");
                sb.Append("\"primaryEvery\":").Append(PrimaryEverySec).Append(",");
                sb.Append("\"secondaryEvery\":").Append(SecondaryEverySec).Append(",");
                sb.Append("\"plan\":\"").Append(plan).Append("\",");
                sb.Append("\"text\":\"").Append(Escape(tray.Text)).Append("\",");
                sb.Append("\"error\":\"").Append(Escape(lastError)).Append("\"");
                sb.Append("}");
                File.WriteAllText(Path.Combine(root, "last.json"), sb.ToString(), new UTF8Encoding(false));
            }
            catch { }
        }

        // quota.js 的报错是中文硬编码，按已知前缀翻译；认不出来的原样返回
        private static string TranslateQuotaError(string err)
        {
            if (string.IsNullOrEmpty(err)) return err;
            if (err.StartsWith("找不到 codex.exe")) return L.T("找不到 codex.exe（Codex 桌面端没装？）");
            if (err.StartsWith("查询超时")) return L.T("查询超时（Codex 没响应或网络不通）");
            if (err.StartsWith("接口没返回额度数据")) return L.T("接口没返回额度数据") + err.Substring("接口没返回额度数据".Length);
            if (err.StartsWith("启动 codex 失败")) return L.T("启动 codex 失败") + err.Substring("启动 codex 失败".Length);
            return err;
        }

        private static string Escape(string s)
        {
            if (s == null) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "").Replace("\n", "\\n");
        }

        // NotifyIcon.Text 最多 63 个字符，超了直接抛异常（英文比中文长，吃过这个亏）
        private static string ClampTray(string s)
        {
            if (s == null) return "";
            if (s.Length <= 63) return s;
            return s.Substring(0, 62) + "…";
        }

        private void SetIcon(Icon icon)
        {
            Icon old = currentIcon;
            tray.Icon = icon;
            currentIcon = icon;
            if (old != null) old.Dispose();
        }

        // ---------- 画图标：原版造型，针对托盘/桌面增强可见性 ----------
        // 保留两段细圆弧、较宽断口、圆头和中心菱形；提亮轨道并恢复柔和三级配色。
        // 桌面保持现有比例；托盘单独略加厚和放大中心。两者均不旋转。
        private static readonly Color TrackColor = Color.FromArgb(120, 129, 139);    // 冷灰蓝 #78818B
        private static readonly Color GreenColor = Color.FromArgb(120, 198, 171);  // 玉石绿 #78C6AB
        private static readonly Color AmberColor = Color.FromArgb(226, 188, 114);  // 暖琥珀 #E2BC72
        private static readonly Color LowColor = Color.FromArgb(229, 140, 136);    // 柔珊瑚 #E58C88
        private static readonly Color DiamondColor = Color.FromArgb(129, 139, 149);// 中性冷灰 #818B95

        private static Color LevelColor(double used)
        {
            if (used <= 25.0) return GreenColor;
            if (used < 75.0) return AmberColor;
            return LowColor; // 剩余 <=25%，即已用 >=75%，按用户要求进入红色。
        }

        private struct GaugeArc
        {
            public double Start, Span, Direction, StartX, StartY, EndX, EndY;
        }

        private static GaugeArc MakeGaugeArc(double radius, double startDeg, double spanDeg, double direction)
        {
            double start = startDeg * Math.PI / 180.0;
            double span = spanDeg * Math.PI / 180.0;
            double end = start + direction * span;
            return new GaugeArc { Start = start, Span = span, Direction = direction,
                StartX = radius * Math.Cos(start), StartY = radius * Math.Sin(start),
                EndX = radius * Math.Cos(end), EndY = radius * Math.Sin(end) };
        }

        // 到圆弧中心线的距离；端点采用真圆帽，与原版细线描边一致。
        private static double ArcDistanceSquared(GaugeArc arc, double x, double y, double angle, double radialDistance2)
        {
            double traveled = arc.Direction * (angle - arc.Start);
            while (traveled < 0.0) traveled += Math.PI * 2.0;
            while (traveled >= Math.PI * 2.0) traveled -= Math.PI * 2.0;
            if (traveled <= arc.Span) return radialDistance2;
            double sx = x - arc.StartX, sy = y - arc.StartY;
            double ex = x - arc.EndX, ey = y - arc.EndY;
            return Math.Min(sx * sx + sy * sy, ex * ex + ey * ey);
        }

        public static Bitmap RenderGauge(int size, double pUsed, double sUsed, bool hasData)
        {
            return RenderGaugeCore(size, pUsed, sUsed, hasData, false);
        }

        internal static Bitmap RenderTrayGauge(int size, double pUsed, double sUsed, bool hasData)
        {
            return RenderGaugeCore(size, pUsed, sUsed, hasData, true);
        }

        private static Bitmap RenderGaugeCore(int size, double pUsed, double sUsed, bool hasData, bool tray)
        {
            var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            double center = size / 2.0;
            // 托盘使用 32px 后由系统缩小，和 16/24px 资源一并做小尺寸光学校正。
            // 加粗时向内收，外径保持原样，避免裁边或把原版造型撑大。
            double halfStroke = size * (tray ? 1.5 : size <= 32 ? 1.3 : 1.15) / 24.0;
            double radius = size * (tray ? 12.0 : 11.9) / 24.0 - halfStroke;
            double halfStroke2 = halfStroke * halfStroke;
            double diamond = size * (tray ? 5.4 : 5.1) / 24.0;
            double p = NormalizeUsed(pUsed, hasData), s = NormalizeUsed(sUsed, hasData);
            // 截图中的上弧约 100°、下弧约 200°；两侧角间隔各约 30°。
            GaugeArc top = MakeGaugeArc(radius, -40.0, 100.0, -1.0);
            GaugeArc bottom = MakeGaugeArc(radius, -10.0, 200.0, 1.0);
            GaugeArc topColor = MakeGaugeArc(radius, -40.0 - 100.0 * p / 100.0, 100.0 * (1.0 - p / 100.0), -1.0);
            GaugeArc bottomColor = MakeGaugeArc(radius, -10.0 + 200.0 * s / 100.0, 200.0 * (1.0 - s / 100.0), 1.0);
            Color pColor = LevelColor(p), sColor = LevelColor(s);
            int samples = size <= 48 ? 8 : 4;
            int count = samples * samples;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                int coverage = 0, red = 0, green = 0, blue = 0;
                for (int sy = 0; sy < samples; sy++)
                for (int sx = 0; sx < samples; sx++)
                {
                    double dx = x + (sx + 0.5) / samples - center;
                    double dy = y + (sy + 0.5) / samples - center;
                    Color color;
                    if (Math.Abs(dx) + Math.Abs(dy) <= diamond) color = DiamondColor;
                    else
                    {
                        double r = Math.Sqrt(dx * dx + dy * dy);
                        if (Math.Abs(r - radius) > halfStroke) continue;
                        double angle = Math.Atan2(dy, dx);
                        double radial2 = (r - radius) * (r - radius);
                        bool upper = ArcDistanceSquared(top, dx, dy, angle, radial2) <= halfStroke2;
                        if (!upper && ArcDistanceSquared(bottom, dx, dy, angle, radial2) > halfStroke2) continue;
                        bool colored = hasData && (upper ? p < 100.0 && ArcDistanceSquared(topColor, dx, dy, angle, radial2) <= halfStroke2
                            : s < 100.0 && ArcDistanceSquared(bottomColor, dx, dy, angle, radial2) <= halfStroke2);
                        color = colored ? (upper ? pColor : sColor) : TrackColor;
                    }
                    coverage++; red += color.R; green += color.G; blue += color.B;
                }
                if (coverage != 0) bmp.SetPixel(x, y, Color.FromArgb(
                    (coverage * 255 + count / 2) / count,
                    (red + coverage / 2) / coverage, (green + coverage / 2) / coverage, (blue + coverage / 2) / coverage));
            }
            return bmp;
        }

        private static double NormalizeUsed(double used, bool hasData)
        {
            if (!hasData || double.IsNaN(used) || double.IsInfinity(used)) return 100.0;
            return Math.Max(0.0, Math.Min(100.0, used));
        }

        // ---------- 单额度（Pro）：整圈一个环 ----------
        // 线宽、半径、中心菱形、三级配色全部沿用双环那套算式，一个参数都不另设。
        // 唯一区别：上下两段之间那两条 30° 断口被接上，变成连续的 360° 整圈。
        // 消耗从 12 点开始顺时针走满一圈：灰色 = 已用，彩色 = 剩余（与界面一律报剩余一致）。
        internal static Bitmap RenderSingleGauge(int size, double used, bool hasData, bool tray)
        {
            var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            double center = size / 2.0;
            double halfStroke = size * (tray ? 1.5 : size <= 32 ? 1.3 : 1.15) / 24.0;
            double radius = size * (tray ? 12.0 : 11.9) / 24.0 - halfStroke;
            double halfStroke2 = halfStroke * halfStroke;
            double diamond = size * (tray ? 5.4 : 5.1) / 24.0;
            double u = NormalizeUsed(used, hasData);
            // 整圈从 12 点（-90°）顺时针；已用多少度，剩余就从那里开始画多少度。
            GaugeArc remain = MakeGaugeArc(radius, -90.0 + 360.0 * u / 100.0, 360.0 * (1.0 - u / 100.0), 1.0);
            Color levelColor = LevelColor(u);
            int samples = size <= 48 ? 8 : 4;
            int count = samples * samples;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                int coverage = 0, red = 0, green = 0, blue = 0;
                for (int sy = 0; sy < samples; sy++)
                for (int sx = 0; sx < samples; sx++)
                {
                    double dx = x + (sx + 0.5) / samples - center;
                    double dy = y + (sy + 0.5) / samples - center;
                    Color color;
                    if (Math.Abs(dx) + Math.Abs(dy) <= diamond) color = DiamondColor;
                    else
                    {
                        double r = Math.Sqrt(dx * dx + dy * dy);
                        if (Math.Abs(r - radius) > halfStroke) continue;
                        double angle = Math.Atan2(dy, dx);
                        double radial2 = (r - radius) * (r - radius);
                        bool colored = hasData && u < 100.0
                            && ArcDistanceSquared(remain, dx, dy, angle, radial2) <= halfStroke2;
                        color = colored ? levelColor : TrackColor;
                    }
                    coverage++; red += color.R; green += color.G; blue += color.B;
                }
                if (coverage != 0) bmp.SetPixel(x, y, Color.FromArgb(
                    (coverage * 255 + count / 2) / count,
                    (red + coverage / 2) / coverage, (green + coverage / 2) / coverage, (blue + coverage / 2) / coverage));
            }
            return bmp;
        }

        // 自检：用假数据把三档 × 两种套餐的触发过程跑一遍，写进文本，不弹任何窗口。
        internal static void WriteNotifySelfTest(string path)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("额度提醒自检（只算逻辑，不弹窗）");
            sb.AppendLine();
            double[] seq = new double[] { 0, 10, 24, 25, 26, 40, 50, 60, 74, 75, 76, 88, 90, 91, 96 };
            string[] names = new string[] { "低档", "中档", "高档" };
            for (int lv = 0; lv <= 2; lv++)
            {
                sb.AppendLine("===== " + names[lv] + " =====");
                sb.AppendLine("-- Plus：5 小时额度（按档位）--");
                RunNotifySeq(sb, NotifyRules.Tier(lv), seq, 1000);
                sb.AppendLine("-- Plus：7 天额度（固定，与档位无关）--");
                RunNotifySeq(sb, NotifyRules.LongFixed(), seq, 2000);
                sb.AppendLine("-- Pro：7 天额度（按档位）--");
                RunNotifySeq(sb, NotifyRules.Tier(lv), seq, 3000);
                sb.AppendLine();
            }

            sb.AppendLine("===== 抖动：24.9 → 25.1 → 24.9 → 25.2（只能响一次）=====");
            RunNotifySeq(sb, NotifyRules.Tier(0), new double[] { 24.9, 25.1, 24.9, 25.2, 25.3 }, 4000);
            sb.AppendLine();
            sb.AppendLine("===== 重置：30 → 60 → 96 → 0 → 26 → 96（归零后要能重新响）=====");
            RunNotifySeq(sb, NotifyRules.Tier(0), new double[] { 30, 60, 96, 0, 26, 96 }, 5000);
            sb.AppendLine();
            sb.AppendLine("===== 启动基线：直接开机处于 80% 时不应立刻弹 =====");
            RunNotifySeq(sb, NotifyRules.Tier(0), new double[] { 80, 81, 96 }, 6000);

            sb.AppendLine();
            sb.AppendLine("===== 耗尽：98 → 100 只响一次，重复 100 不再响，归零后能再响 =====");
            RunNotifySeq(sb, NotifyRules.Tier(0), new double[] { 98, 100, 100, 100, 0, 100 }, 7000);

            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        }

        private static void RunNotifySeq(StringBuilder sb, int[] th, double[] seq, double resetsAt)
        {
            NotifyWindow w = new NotifyWindow();
            w.thresholds = th;
            sb.Append("  阈值[");
            for (int i = 0; i < th.Length; i++)
            {
                if (i > 0) sb.Append(",");
                sb.Append(th[i]);
            }
            sb.Append("]  ");
            foreach (double v in seq)
            {
                int t = w.Fire(v, resetsAt);
                sb.Append(v.ToString("0.#"));
                if (w.Exhausted) sb.Append("★耗尽");
                else if (t > 0) sb.Append("★响(" + t + "%)");
                sb.Append("  ");
            }
            sb.AppendLine();
        }

        // 桌面测试脚本用：按类型弹一次，配上对应的提示音
        internal static void RunToastDemo(string kind, string slot, string theme = null, string settingsDir = null)
        {
            string dir = Path.GetDirectoryName(Application.ExecutablePath);
            AppSettings prefs = AppSettings.Load(settingsDir ?? dir);
            L.AutoDetect();
            if (prefs.language != "auto") L.Lang = prefs.language;
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            UiTheme.Set(theme == "light" || theme == "dark" ? theme : prefs.theme);

            string body, sound;
            bool dailyPaceDemo = kind == "daily-pace";
            bool restored = kind == "5h-restored" || kind == "7d-restored";
            bool compactToast = restored || dailyPaceDemo;
            bool exhausted = kind == "5h-empty" || kind == "7d-empty";
            bool weekly = kind == "7d-empty" || kind == "7d-25" || kind == "7d-restored";
            string window = weekly ? L.F("{0} 天", 7) : L.T("5 小时");
            if (dailyPaceDemo)
            {
                body = NotifyRules.DailyPaceBody();
                sound = NoSound;
            }
            else if (restored)
            {
                body = NotifyRules.RecoveryBody(weekly ? 7 : 0);
                sound = RecoverySound;
            }
            else if (exhausted)
            {
                body = L.F("{0}额度已耗尽", window);
                sound = weekly ? "notify-7d-empty.wav" : "notify-5h-empty.wav";
            }
            else
            {
                int remaining = kind == "10" ? 10 : 25;
                body = L.F("{0}剩余 {1}%", window, remaining) + "\n" +
                    MultiTooltipCountdown(DateTimeOffset.UtcNow.AddMinutes(30).ToUnixTimeSeconds());
                sound = "notify.wav";
            }

            using (ToastForm form = new ToastForm(L.T("额度提醒"), body, slot, "22***@example.com", compactToast))
            {
                AttachToastSound(form, dir, sound, Console.Error.WriteLine, prefs.notifySound, prefs.notifyVolume);
                Application.Run(form);
            }
        }

        // 探测本机 codex.exe 落在哪，写进文本，方便远程排查
        internal static void WriteCodexProbe(string path, string folder)
        {
            StringBuilder sb = new StringBuilder();
            if (!string.IsNullOrEmpty(folder))
            {
                sb.AppendLine("folder=" + folder + " exists=" + Directory.Exists(folder));
                string inFolder = CodexFinder.FindInFolder(folder);
                sb.AppendLine("inFolder=" + (string.IsNullOrEmpty(inFolder) ? "(none)" : inFolder));
            }
            string found = CodexFinder.Find();
            sb.AppendLine("found=" + (string.IsNullOrEmpty(found) ? "(none)" : found));
            sb.AppendLine("CODEX_CLI_PATH=" + (Environment.GetEnvironmentVariable("CODEX_CLI_PATH") ?? "(unset)"));

            string bin = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                @"OpenAI\Codex\bin");
            sb.AppendLine("localappdata-bin=" + bin + " exists=" + Directory.Exists(bin));

            string apps = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WindowsApps");
            sb.AppendLine("windowsapps=" + apps + " exists=" + Directory.Exists(apps));

            try
            {
                foreach (Process p in Process.GetProcesses())
                {
                    try
                    {
                        if (p.ProcessName.IndexOf("codex", StringComparison.OrdinalIgnoreCase) < 0) continue;
                        string exe = null;
                        try { exe = p.MainModule.FileName; } catch { }
                        sb.AppendLine("proc " + p.ProcessName + " pid=" + p.Id + " exe=" + (exe ?? "(denied)"));
                    }
                    catch { }
                    finally { try { p.Dispose(); } catch { } }
                }
            }
            catch { }

            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        }

        // 出识别界面的截图
        internal static void WriteLocatePreview(string dir, string pngPath)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Directory.CreateDirectory(dir);
            CodexLocateForm f = new CodexLocateForm("");
            f.Show();
            for (int i = 0; i < 20; i++) { Application.DoEvents(); Thread.Sleep(30); }

            Rectangle r = f.Bounds;
            int pad = 16;
            double scale = 1.0;
            if (!Program.DpiAware)
            {
                try
                {
                    uint dpi = GetDpiForWindow(f.Handle);
                    if (dpi >= 96) scale = dpi / 96.0;
                }
                catch { }
            }

            int pw = (int)Math.Round((r.Width + pad * 2) * scale);
            int ph = (int)Math.Round((r.Height + pad * 2) * scale);
            int px = (int)Math.Round((r.Left - pad) * scale);
            int py = (int)Math.Round((r.Top - pad) * scale);
            using (Bitmap bmp = new Bitmap(pw, ph))
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.CopyFromScreen(px, py, 0, 0, new Size(pw, ph));
                bmp.Save(pngPath, ImageFormat.Png);
            }
            f.Close();
            f.Dispose();
        }

        // 真弹一次，让人看动画：滑进来 → 停 7 秒 → 滑回去
        internal static void RunToastLive(string wavPath)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            L.AutoDetect();
            UiTheme.Set("auto");
            ToastForm f = new ToastForm(L.T("额度提醒"), L.T("5 小时额度只剩 10%｜1 小时 51 分后恢复"), "a");
            AttachToastSound(f, Path.GetDirectoryName(Application.ExecutablePath), Path.GetFileName(wavPath), Console.Error.WriteLine);
            f.Show();
            DateTime until = DateTime.Now.AddSeconds(9);
            while (DateTime.Now < until)
            {
                Application.DoEvents();
                Thread.Sleep(20);
            }
        }

        // 出图：把自绘弹窗显示出来截图，同时放一次提示音，用于检查效果
        internal static void WriteToastPreview(string dir, string wavPath)
        {
            Directory.CreateDirectory(dir);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            L.AutoDetect();
            UiTheme.Set("auto");
            ToastForm f = new ToastForm(L.T("额度提醒"), L.T("5 小时额度还剩 19%｜2 小时 22 分后恢复"), "a");
            AttachToastSound(f, Path.GetDirectoryName(Application.ExecutablePath), Path.GetFileName(wavPath), Console.Error.WriteLine);
            f.Show();
            for (int i = 0; i < 24; i++)
            {
                Application.DoEvents();
                Thread.Sleep(30);
            }

            Rectangle r = f.Bounds;
            int pad = 16;
            // 本进程没声明 DPI 感知，窗体坐标是逻辑像素；截屏要用物理像素，
            // 所以按窗口 DPI 换算一下，否则截出来的位置会整体偏移。
            double scale = 1.0;
            if (!Program.DpiAware)
            {
                try
                {
                    uint dpi = GetDpiForWindow(f.Handle);
                    if (dpi >= 96) scale = dpi / 96.0;
                }
                catch { }
            }

            int pw = (int)Math.Round((r.Width + pad * 2) * scale);
            int ph = (int)Math.Round((r.Height + pad * 2) * scale);
            int px = (int)Math.Round((r.Left - pad) * scale);
            int py = (int)Math.Round((r.Top - pad) * scale);
            using (Bitmap bmp = new Bitmap(pw, ph))
            using (Graphics g = Graphics.FromImage(bmp))
            using (Bitmap toast = f.RenderCurrentSurface())
            {
                g.CopyFromScreen(px, py, 0, 0, new Size(pw, ph));
                // Live notifications are excluded from desktop capture to keep the backdrop clean.
                g.DrawImageUnscaled(toast, (int)Math.Round(pad * scale), (int)Math.Round(pad * scale));
                bmp.Save(Path.Combine(dir, "弹窗效果.png"), ImageFormat.Png);
            }

            f.Close();
            f.Dispose();
        }

        // 出图：现版双环 + Pro 单环的消耗过程，浅色与深色各一行，用于对照。
        internal static void WriteProPreview(string dir)
        {
            Directory.CreateDirectory(dir);
            string[] labels = new string[] { "现版 双环 40/40", "Pro 已用 40%", "Pro 已用 0%",
                "Pro 已用 25%", "Pro 已用 50%", "Pro 已用 75%", "Pro 已用 100%" };
            int cell = 200, gap = 16, labelH = 34;
            int cols = labels.Length;
            int width = cols * (cell + gap) + gap;
            int rowH = cell + labelH;
            int height = rowH * 2 + gap * 3;
            Color[] bgs = new Color[] { Color.FromArgb(244, 245, 247), Color.FromArgb(32, 34, 38) };
            Color[] fgs = new Color[] { Color.FromArgb(40, 44, 52), Color.FromArgb(228, 232, 238) };

            using (var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb))
            using (var g = Graphics.FromImage(bmp))
            using (var font = new Font("Microsoft YaHei", 10.5f))
            {
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                for (int row = 0; row < 2; row++)
                {
                    int top = gap + row * (rowH + gap);
                    using (var bg = new SolidBrush(bgs[row]))
                        g.FillRectangle(bg, 0, top - gap / 2, width, rowH + gap);
                    for (int i = 0; i < cols; i++)
                    {
                        int left = gap + i * (cell + gap);
                        double used = i == 1 ? 40.0 : (i - 2) * 25.0;
                        using (Bitmap tile = i == 0
                            ? RenderGauge(cell, 40, 40, true)
                            : RenderSingleGauge(cell, used, true, false))
                        {
                            g.DrawImageUnscaled(tile, left, top);
                        }
                        using (var fg = new SolidBrush(fgs[row]))
                            g.DrawString(labels[i], font, fg, left, top + cell + 6);
                    }
                }
                bmp.Save(Path.Combine(dir, "Pro单环预览.png"), ImageFormat.Png);
            }

            // 托盘实际就是 32px，放大 6 倍看一眼：整圈在小尺寸下会不会糊成一坨。
            int s32 = 32, zoom = 6, tw = s32 * zoom, tgap = 14, tlabel = 30;
            string[] tlabels = new string[] { "现版 32px", "Pro 0%", "Pro 25%", "Pro 50%", "Pro 75%", "Pro 100%" };
            int tcols = tlabels.Length;
            int twidth = tcols * (tw + tgap) + tgap;
            int theight = tw + tlabel + tgap * 2;
            using (var tb = new Bitmap(twidth, theight, PixelFormat.Format32bppArgb))
            using (var tg = Graphics.FromImage(tb))
            using (var tfont = new Font("Microsoft YaHei", 9.5f))
            {
                tg.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                tg.Clear(Color.FromArgb(32, 34, 38));
                tg.InterpolationMode = InterpolationMode.NearestNeighbor;
                tg.PixelOffsetMode = PixelOffsetMode.Half;
                for (int i = 0; i < tcols; i++)
                {
                    int left = tgap + i * (tw + tgap);
                    double tu = (i - 1) * 25.0;
                    using (Bitmap t = i == 0
                        ? RenderTrayGauge(s32, 40, 40, true)
                        : RenderSingleGauge(s32, tu, true, true))
                    {
                        tg.DrawImage(t, new Rectangle(left, tgap, tw, tw));
                    }
                    using (var fg = new SolidBrush(Color.FromArgb(228, 232, 238)))
                        tg.DrawString(tlabels[i], tfont, fg, left, tgap + tw + 4);
                }
                tb.Save(Path.Combine(dir, "Pro单环-托盘尺寸.png"), ImageFormat.Png);
            }
        }

        // 单额度（Pro）：托盘上就是一个环
        private static Icon DrawSingleIcon(double used, bool hasData)
        {
            using (var bmp = RenderSingleGauge(32, used, hasData, true))
            {
                IntPtr h = bmp.GetHicon();
                try
                {
                    using (var tmp = Icon.FromHandle(h))
                    {
                        return (Icon)tmp.Clone();
                    }
                }
                finally
                {
                    DestroyIcon(h);
                }
            }
        }

        private static Icon DrawIcon(double pUsed, double sUsed, bool hasData)
        {
            var bmp = RenderTrayGauge(32, pUsed, sUsed, hasData);
            try
            {
                IntPtr h = bmp.GetHicon();
                try
                {
                    using (var tmp = Icon.FromHandle(h))
                    {
                        return (Icon)tmp.Clone();
                    }
                }
                finally
                {
                    DestroyIcon(h);
                }
            }
            finally
            {
                bmp.Dispose();
            }
        }

        /// <summary>把同一个图案写成多尺寸 .ico，供 exe 和桌面快捷方式使用。</summary>
        public static void WriteIco(string path, double pUsed, double sUsed, bool single, bool hasQuota = true)
        {
            int[] sizes = new int[] { 16, 24, 32, 48, 64, 128, 256 };
            var pngs = new System.Collections.Generic.List<byte[]>();

            foreach (int s in sizes)
            {
                using (var bmp = single ? RenderSingleGauge(s, sUsed, hasQuota, false) : RenderGauge(s, pUsed, sUsed, hasQuota))
                using (var ms = new MemoryStream())
                {
                    bmp.Save(ms, ImageFormat.Png);
                    pngs.Add(ms.ToArray());
                }
            }

            using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write))
            using (var bw = new BinaryWriter(fs))
            {
                bw.Write((short)0);                 // reserved
                bw.Write((short)1);                 // type = icon
                bw.Write((short)sizes.Length);      // count

                int offset = 6 + 16 * sizes.Length;
                for (int i = 0; i < sizes.Length; i++)
                {
                    int s = sizes[i];
                    bw.Write((byte)(s >= 256 ? 0 : s));   // width
                    bw.Write((byte)(s >= 256 ? 0 : s));   // height
                    bw.Write((byte)0);                    // palette
                    bw.Write((byte)0);                    // reserved
                    bw.Write((short)1);                   // color planes
                    bw.Write((short)32);                  // bpp
                    bw.Write(pngs[i].Length);
                    bw.Write(offset);
                    offset += pngs[i].Length;
                }
                for (int i = 0; i < sizes.Length; i++)
                {
                    bw.Write(pngs[i]);
                }
                bw.Flush();
            }
        }

        // ---------- 详情窗口 ----------
        private void ShowDetail() { ShowSettingsPage("accounts"); }
        private void ShowDetailPreview()
        {
            if (accountDiscoveryPending) { pendingDetails = true; return; }
            string detailAccountSlot = viewAccountSlot;
            string detailAccountLabel = viewAccountLabel;
            if (!hasData)
            {
                string body = string.IsNullOrEmpty(lastError)
                    ? L.T("正在读取额度，请稍等一下。")
                    : L.F("还没拿到额度数据。\r\n\r\n原因：{0}", lastError);
                MessageBox.Show(body, L.T("Codex 额度"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            detailDark = IsDarkMode();
            detailBg = detailDark ? Color.FromArgb(32, 32, 32) : SystemColors.Window;
            detailFg = detailDark ? Color.FromArgb(235, 235, 235) : SystemColors.ControlText;

            using (Form f = new Form())
            {
                f.Text = string.IsNullOrEmpty(detailAccountLabel) ? L.T("Codex 额度") : detailAccountLabel + " · " + L.T("额度详情");
                f.FormBorderStyle = FormBorderStyle.FixedDialog;
                f.StartPosition = FormStartPosition.CenterScreen;
                f.MaximizeBox = false;
                f.MinimizeBox = false;
                f.ShowIcon = false;
                f.Font = SystemFonts.MessageBoxFont;
                f.ClientSize = new Size(420, 322);
                f.BackColor = detailBg;
                f.ForeColor = detailFg;
                if (detailDark)
                {
                    f.HandleCreated += delegate { SetDarkTitleBar(f.Handle); };
                }

                Label planLabel = new Label();
                planLabel.Text = string.IsNullOrEmpty(detailAccountLabel) ? L.F("套餐：{0}", plan) : detailAccountLabel;
                planLabel.Location = new Point(18, 16);
                planLabel.AutoSize = true;
                planLabel.BackColor = detailBg;
                planLabel.ForeColor = detailFg;
                f.Controls.Add(planLabel);

                // Pro 只有一个周额度：详情窗就一块，不摆"不限制"的空块
                bool shortOnly = multiMode && primaryUsed >= 0 && secondaryUsed < 0;
                bool singleWindow = shortOnly || (primaryUsed < 0 && secondaryUsed >= 0);
                int btnY = 272;
                if (singleWindow)
                {
                    f.ClientSize = new Size(420, 216);
                    btnY = 162;
                    f.Controls.Add(BuildQuotaGroup(shortOnly ? L.T("5 小时额度") : L.F("{0} 天额度", secondaryDays),
                        shortOnly ? primaryUsed : secondaryUsed, shortOnly ? primaryReset : secondaryReset,
                        new Point(16, 46), !shortOnly));
                }
                else
                {
                    f.Controls.Add(BuildQuotaGroup(L.T("5 小时额度"), primaryUsed, primaryReset,
                        new Point(16, 46), false));
                    f.Controls.Add(BuildQuotaGroup(L.F("{0} 天额度", secondaryDays), secondaryUsed, secondaryReset,
                        new Point(16, 156), true));
                }

                if (!string.IsNullOrEmpty(lastError))
                {
                    Label failure = new Label();
                    failure.Text = L.T("更新失败") + " · " + L.T("最后成功") + " " +
                        (lastOkAt == DateTime.MinValue ? L.T("暂无") : lastOkAt.ToString("MM-dd HH:mm"));
                    failure.SetBounds(18, btnY, 384, 36);
                    failure.ForeColor = detailDark ? Color.FromArgb(255, 180, 120) : Color.DarkRed;
                    f.Controls.Add(failure);
                    btnY += 40;
                    f.ClientSize = new Size(f.ClientSize.Width, f.ClientSize.Height + 40);
                }

                Button refreshBtn = new Button();
                refreshBtn.Text = L.T("刷新");
                refreshBtn.Location = new Point(212, btnY);
                refreshBtn.Size = new Size(90, 32);
                refreshBtn.DialogResult = DialogResult.OK;
                f.Controls.Add(refreshBtn);

                Button closeBtn = new Button();
                closeBtn.Text = L.T("关闭");
                closeBtn.Location = new Point(312, btnY);
                closeBtn.Size = new Size(90, 32);
                closeBtn.DialogResult = DialogResult.Cancel;
                f.Controls.Add(closeBtn);

                f.AcceptButton = refreshBtn;
                f.CancelButton = closeBtn;

                if (detailDark)
                {
                    Button[] btns = new Button[] { refreshBtn, closeBtn };
                    foreach (Button b in btns)
                    {
                        b.FlatStyle = FlatStyle.Flat;
                        b.FlatAppearance.BorderColor = Color.FromArgb(84, 84, 88);
                        b.BackColor = Color.FromArgb(48, 48, 52);
                        b.ForeColor = detailFg;
                    }
                }

                if (f.ShowDialog() == DialogResult.OK)
                {
                    if (multiMode && multiAccounts != null && !string.IsNullOrEmpty(detailAccountSlot)) multiAccounts.RefreshAccount(detailAccountSlot);
                    else refreshNow.Set();
                }
            }
        }

        // 一块额度区域，全用系统标准控件（GroupBox + Label + ProgressBar）
        private GroupBox BuildQuotaGroup(string title, double used, double reset, Point at, bool weekly)
        {
            GroupBox box = new GroupBox();
            box.Text = title;
            box.Location = at;
            box.Size = new Size(388, 100);
            box.BackColor = detailBg;
            box.ForeColor = detailFg;

            if (used < 0)
            {
                Label na = new Label();
                na.Text = weekly ? L.T("暂无数据") : L.T("不限制（当前套餐不含这项限额）");
                na.Location = new Point(16, 30);
                na.AutoSize = true;
                na.BackColor = detailBg;
                na.ForeColor = detailFg;
                box.Controls.Add(na);
                return box;
            }

            int remain = (int)Math.Round(100 - used);
            if (remain < 0) remain = 0;
            if (remain > 100) remain = 100;

            Label pct = new Label();
            pct.Text = L.F("剩余 {0}%", remain);
            pct.Location = new Point(16, 24);
            pct.AutoSize = true;
            pct.BackColor = detailBg;
            pct.ForeColor = detailFg;
            box.Controls.Add(pct);

            ProgressBar bar = new ProgressBar();
            bar.Minimum = 0;
            bar.Maximum = 100;
            bar.Value = remain;
            bar.Location = new Point(16, 46);
            bar.Size = new Size(356, 20);
            box.Controls.Add(bar);

            Label resetLabel = new Label();
            resetLabel.Text = L.F("恢复：{0}", FormatReset(reset));
            resetLabel.Location = new Point(16, 72);
            resetLabel.AutoSize = true;
            resetLabel.BackColor = detailBg;
            resetLabel.ForeColor = detailFg;
            box.Controls.Add(resetLabel);

            return box;
        }

        // 托盘专用：英文去掉 "resets in" 前缀，否则两条加起来超过 63 字符
        private static string CountdownTray(double unixSec)
        {
            if (L.Lang == "zh") return Countdown(unixSec);
            bool ja = L.Lang == "ja";
            if (unixSec <= 0) return ja ? "不明" : "unknown";
            DateTime dt = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                .AddSeconds(unixSec).ToLocalTime();
            TimeSpan left = dt - DateTime.Now;
            if (left.TotalSeconds <= 0) return ja ? "まもなく" : "now";
            if (left.TotalDays >= 1)
                return ja
                    ? "あと" + (int)left.TotalDays + "日" + left.Hours + "時間"
                    : (int)left.TotalDays + "d " + left.Hours + "h";
            if (left.TotalHours >= 1)
                return ja
                    ? "あと" + left.Hours + "時間" + left.Minutes + "分"
                    : left.Hours + "h " + left.Minutes + "m";
            return ja
                ? "あと" + Math.Max(1, (int)left.TotalMinutes) + "分"
                : Math.Max(1, (int)left.TotalMinutes) + "m";
        }

        // 悬停提示用：距离恢复还有多久（不带刷新频率）
        private static string Countdown(double unixSec)
        {
            if (unixSec <= 0) return L.T("恢复时间未知");
            DateTime dt = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                .AddSeconds(unixSec).ToLocalTime();
            TimeSpan left = dt - DateTime.Now;
            if (left.TotalSeconds <= 0) return L.T("即将恢复");

            if (left.TotalDays >= 1) return L.F("{0} 天 {1} 小时后恢复", (int)left.TotalDays, left.Hours);
            if (left.TotalHours >= 1) return L.F("{0} 小时 {1} 分后恢复", left.Hours, left.Minutes);
            return L.F("{0} 分钟后恢复", Math.Max(1, (int)left.TotalMinutes));
        }

        private static double ToUnix(DateTime utc)
        {
            return (utc - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
        }

        private static string FormatReset(double unixSec)
        {
            if (unixSec <= 0) return L.T("未知");
            DateTime dt = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                .AddSeconds(unixSec).ToLocalTime();
            TimeSpan left = dt - DateTime.Now;
            if (left.TotalSeconds < 0) return L.F("{0}（已到期）", dt.ToString("MM-dd HH:mm"));

            string remain;
            if (left.TotalDays >= 1) remain = L.F("{0} 天 {1} 小时", (int)left.TotalDays, left.Hours);
            else if (left.TotalHours >= 1) remain = L.F("{0} 小时 {1} 分", left.Hours, left.Minutes);
            else remain = L.F("{0} 分钟", left.Minutes);

            return L.F("{0}（还有 {1}）", dt.ToString("MM-dd HH:mm"), remain);
        }

        private void Log(string msg)
        {
            try
            {
                File.AppendAllText(logPath,
                    DateTime.Now.ToString("MM-dd HH:mm:ss") + " " + msg + "\r\n",
                    new UTF8Encoding(false));
            }
            catch { }
        }

        private void Shutdown() { Dispose(); ExitThread(); }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !contextDisposed)
            {
                contextDisposed = true;
                try { stopWorker.Set(); } catch { }
                try { if (accountForm != null && !accountForm.IsDisposed) accountForm.StopForExit(); } catch { }
                try { if (multiAccounts != null) multiAccounts.Dispose(); } catch { }
                try { if (quotaTip != null && !quotaTip.IsDisposed) quotaTip.Dispose(); } catch { }
                try { if (quotaSettingsForm != null && !quotaSettingsForm.IsDisposed) quotaSettingsForm.Close(); } catch { }
                try { if (accountForm != null && !accountForm.IsDisposed) accountForm.Close(); } catch { }
                try { accountsRequest.Dispose(); settingsRequest.Dispose(); displayRequest.Dispose(); detailsRequest.Dispose(); exitRequest.Dispose(); groupTestRequest.Dispose(); } catch { }
                try { uiTimer.Stop(); uiTimer.Dispose(); } catch { }
                try { if (tray != null) { tray.Visible = false; tray.Dispose(); } } catch { }
                try { if (currentIcon != null) currentIcon.Dispose(); } catch { }
                Log("已退出");
            }
            base.Dispose(disposing);
        }
    }
}
