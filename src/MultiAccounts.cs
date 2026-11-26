using System;
using System.Collections.Generic;
#if !DISPLAY_POLICY_ONLY
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;
#endif

namespace QuotaTrayApp
{
    internal sealed class DisplayAccount
    {
        internal readonly string Slot, Plan;
        internal readonly bool QueryFailed;
        internal DisplayAccount(string slot, string plan) : this(slot, plan, false) { }
        internal DisplayAccount(string slot, string plan, bool queryFailed) { Slot = slot; Plan = plan; QueryFailed = queryFailed; }
    }
    internal sealed class DisplayPreferences
    {
        public string[] traySlots;
        public string desktopSlot;
        public DisplayPreferences() { }
        internal DisplayPreferences(string[] slots, string desktop) { traySlots = slots; desktopSlot = desktop; }
    }
    internal sealed class AccountToastEntry
    {
        internal string Slot, Label, Body, Sound, EmailMask;
        internal bool Compact, PlainTitle;
    }
    internal sealed class AccountToastQueue
    {
        private readonly Queue<AccountToastEntry> entries = new Queue<AccountToastEntry>();
        internal void Enqueue(string label, string body, string sound) { Enqueue(null, label, body, sound); }
        internal void Enqueue(string slot, string label, string body, string sound, string emailMask = null, bool compact = false, bool plainTitle = false)
        {
            entries.Enqueue(new AccountToastEntry { Slot = slot, Label = label, Body = body, Sound = sound, EmailMask = emailMask, Compact = compact, PlainTitle = plainTitle });
        }
        internal void Remove(string slot)
        {
            int count = entries.Count;
            for (int i = 0; i < count; i++) { AccountToastEntry entry = entries.Dequeue(); if (entry.Slot != slot) entries.Enqueue(entry); }
        }
        internal AccountToastEntry Take(bool active) { return active || entries.Count == 0 ? null : entries.Dequeue(); }
        internal void Clear() { entries.Clear(); }
    }
    internal static class DisplayPolicy
    {
        internal static bool ValidSlot(string slot) { return slot != null && slot.Length == 1 && slot[0] >= 'a' && slot[0] <= 'e'; }
        internal static bool IsValid(IList<DisplayAccount> accounts, DisplayPreferences prefs)
        {
            if (accounts == null || accounts.Count == 0 || accounts.Count > 5 || prefs == null || prefs.traySlots == null || prefs.traySlots.Length == 0 || prefs.traySlots.Length > 5) return false;
            var connected = new HashSet<string>();
            foreach (DisplayAccount a in accounts) if (a == null || !ValidSlot(a.Slot) || !connected.Add(a.Slot)) return false;
            var selected = new HashSet<string>();
            foreach (string slot in prefs.traySlots) if (!connected.Contains(slot) || !selected.Add(slot)) return false;
            return prefs.desktopSlot == "auto" || (ValidSlot(prefs.desktopSlot) && connected.Contains(prefs.desktopSlot));
        }
        internal static string ChooseDesktop(IList<DisplayAccount> accounts, DisplayPreferences prefs)
        {
            if (!IsValid(accounts, prefs)) return null;
            if (prefs.desktopSlot != "auto") return prefs.desktopSlot;
            string best = null; int rank = -1;
            foreach (DisplayAccount a in accounts)
            {
                if (Array.IndexOf(prefs.traySlots, a.Slot) < 0) continue;
                string plan = (a.Plan ?? "").ToLowerInvariant();
                int r = plan.StartsWith("pro", StringComparison.Ordinal) ? 2 : plan == "plus" ? 1 : 0;
                if (r > rank || (r == rank && string.CompareOrdinal(a.Slot, best) < 0)) { best = a.Slot; rank = r; }
            }
            return best;
        }
    }
#if !DISPLAY_POLICY_ONLY
    internal static class AccountOperationGate { internal static readonly object Sync = new object(); }

    internal sealed class MultiAccountController : IDisposable
    {
        private sealed class State
        {
            internal ConnectedSlot account;
            internal ConnectionReply reply;
            internal DateTime lastSuccess;
            internal string error = "not_checked";
            internal NotifyIcon icon;
            internal TrayIconHover hover;
            internal string iconKey;
            internal NotifyWindow tier, fixedWindow;
            internal int level = int.MinValue;
            internal bool single;
            internal DateTime lastCreditDetailUtc = DateTime.MinValue;
            internal int lastCreditCount = -1;
            internal bool forceCreditDetail;
            internal ResetCreditsInfo cachedResetCredits;
        }
        private sealed class WorkResult
        {
            internal string command, slot;
            internal ConnectionReply reply;
            internal ConnectedIdentity expected;
            internal int generation;
            internal bool creditDetailsRequested;
        }
        private readonly TrayContext host;
        private readonly string root, node, codex, script;
        private readonly AppSettings settings;
        private readonly ContextMenuStrip menu;
        private readonly Dictionary<string, State> states = new Dictionary<string, State>();
        private readonly object gate = new object(), processGate = new object();
        private readonly Queue<WorkResult> results = new Queue<WorkResult>();
        private readonly AutoResetEvent wake = new AutoResetEvent(false);
        private readonly Thread worker;
        private volatile bool stopped;
        private bool listNeeded = true, refreshAll = true;
        private readonly bool canQuery;
        private string refreshSlot, selected, desktopSource;
        private int generation;
        private Process active;
        private DisplayPreferences prefs;
        private Form settingsForm;
        private AccountOverviewForm overviewForm;
        private bool groupActive;
        private string groupError;
        private readonly DailyPaceTracker dailyPace;
        private readonly ResetCreditTracker resetCreditsTracker;
        internal bool Managed { get; private set; }
        internal bool HasOpenWindows { get { return overviewForm != null && !overviewForm.IsDisposed; } }

        internal MultiAccountController(TrayContext host, string root, string nodeExe, string codexExe, AppSettings settings, ContextMenuStrip menu)
        {
            this.host = host; this.root = root; node = nodeExe; codex = codexExe; this.settings = settings; this.menu = menu;
            canQuery = !String.IsNullOrEmpty(codex) && File.Exists(codex);
            script = AccountConnectionForm.EnsureScript(root);
            dailyPace = DailyPaceTracker.Load(root);
            resetCreditsTracker = ResetCreditTracker.Load(root);
            try { prefs = new JavaScriptSerializer().Deserialize<DisplayPreferences>(File.ReadAllText(Path.Combine(root, "display-settings.json"), Encoding.UTF8)); } catch { prefs = null; }
            worker = new Thread(WorkerLoop); worker.IsBackground = true; worker.Name = "Multi-account quota"; worker.Start();
        }
        private static bool SameIdentity(ConnectedIdentity a, ConnectedIdentity b)
        {
            return a != null && b != null && !string.IsNullOrEmpty(a.userId) && !string.IsNullOrEmpty(a.accountId) && a.userId == b.userId && a.accountId == b.accountId;
        }
        private static bool ValidWindow(WindowInfo w)
        {
            return w != null && !double.IsNaN(w.used) && !double.IsInfinity(w.used) && w.used >= 0 && w.used <= 100 && !double.IsNaN(w.windowMins) && !double.IsInfinity(w.windowMins) && w.windowMins > 0 && !double.IsNaN(w.resetsAt) && !double.IsInfinity(w.resetsAt) && w.resetsAt >= 0 && w.resetsAt <= 253402300799;
        }
        private static string SafePlan(string value) { return PlanNames.Store(value); }
        private static string Mask(string value)
        {
            return value != null && value.Length <= 128 && Regex.IsMatch(value, @"^[A-Za-z0-9._%+\-]{0,2}\*{3,}@[A-Za-z0-9.\-]{1,100}$") ? value : "***";
        }
        private static string SafeCode(string value)
        {
            switch (value) { case "timeout": case "cancelled": case "identity_mismatch": case "not_connected": case "keyring_unavailable": case "busy": case "account_busy": case "store_busy": case "no_quota_data": case "server_error": case "invalid_response": case "node_start_failed": case "codex_launch_failed": case "auth_failed": return value; default: return "client_error"; }
        }
        private static string ErrorText(string code)
        {
            switch (code) { case "identity_mismatch": return L.T("账号身份变化，已停止读取。"); case "timeout": return L.T("连接超时，请重试。"); case "not_connected": return L.T("请先连接此账号。"); case "keyring_unavailable": return L.T("Windows 凭据库不可用，已停止连接。"); case "auth_failed": return L.T("当前绑定的 Codex 登录不可用，请刷新登录或重新绑定。"); case "node_start_failed": case "codex_launch_failed": return AccountConnectionForm.ConnectionError(code); default: return L.T("额度查询失败"); }
        }
        private static string PlanTitle(string value) { return PlanNames.Title(value); }
        private static int SlotNumber(string slot)
        {
            if (slot != null && slot.Length == 1 && slot[0] >= 'a' && slot[0] <= 'z') return slot[0] - 'a' + 1;
            int number; return Int32.TryParse(slot, out number) && number > 0 ? number : 1;
        }
        private static string AccountMask(State state)
        {
            ConnectedSlot account = state.account;
            return Mask(account.emailMask ?? (account.identity == null ? null : account.identity.emailMask));
        }

        private string LabelFor(State s)
        {
            string plan=PlanTitle(s.account.plan ?? (s.account.identity == null ? null : s.account.identity.plan));
            string email=AccountMask(s);
            return L.F("账号 {0}", SlotNumber(s.account.slot)) + "  |  " + plan + "  |  " + email;
        }
        private List<DisplayAccount> Accounts()
        {
            var list = new List<DisplayAccount>();
            foreach (State s in states.Values) list.Add(new DisplayAccount(s.account.slot, SafePlan(s.account.plan ?? (s.account.identity == null ? null : s.account.identity.plan))));
            return list;
        }
        private void WorkerLoop()
        {
            DateTime next = DateTime.MinValue;
            while (!stopped)
            {
                bool list; string single; bool manualAll; bool all; int gen;
                lock (gate) { list = listNeeded; listNeeded = false; single = refreshSlot; refreshSlot = null; manualAll = refreshAll; all = manualAll || DateTime.UtcNow >= next; refreshAll = false; gen = generation; }
                if (list)
                {
                    ConnectionReply reply = Execute("list", "");
                    lock (gate) results.Enqueue(new WorkResult { command = "list", reply = reply, generation = gen });
                    // Wait for Pump to validate and publish identity bindings before querying.
                    next = DateTime.UtcNow.AddSeconds(30);
                }
                else if (canQuery && (single != null || all))
                {
                    bool manualRefresh = single != null || manualAll;
                    List<ConnectedSlot> snapshot = new List<ConnectedSlot>();
                    var detailFlags = new Dictionary<string, bool>(StringComparer.Ordinal);
                    lock (gate)
                    {
                        foreach (State s in states.Values)
                        {
                            if (single != null && s.account.slot != single) continue;
                            snapshot.Add(s.account);
                            bool needDetails = manualRefresh || s.lastCreditDetailUtc == DateTime.MinValue || s.forceCreditDetail ||
                                (DateTime.UtcNow - s.lastCreditDetailUtc).TotalMinutes >= ResetCreditTracker.DetailIntervalMinutes;
                            detailFlags[s.account.slot] = needDetails;
                        }
                    }
                    snapshot.Sort(delegate(ConnectedSlot a, ConnectedSlot b) { return string.CompareOrdinal(a.slot, b.slot); });
                    foreach (ConnectedSlot account in snapshot)
                    {
                        if (stopped) break;
                        bool includeDetails = detailFlags.ContainsKey(account.slot) && detailFlags[account.slot];
                        ConnectionReply reply = Execute("query", account.slot, includeDetails);
                        lock (gate) results.Enqueue(new WorkResult { command = "query", slot = account.slot, expected = account.identity, reply = reply, generation = gen, creditDetailsRequested = includeDetails });
                    }
                    next = DateTime.UtcNow.AddSeconds(30);
                }
                else wake.WaitOne(500);
            }
        }
        private ConnectionReply Execute(string command, string slot, bool includeCreditDetails = true)
        {
            lock (AccountOperationGate.Sync)
            {
                if (stopped) return new ConnectionReply { code = "cancelled" };
                ConnectionReply result = null; bool bad = false; object outputLock = new object(); int lines = 0;
                try
                {
                    var psi = new ProcessStartInfo(node, "\"" + script + "\" " + command + (slot.Length == 0 ? "" : " " + slot));
                    psi.UseShellExecute = false; psi.CreateNoWindow = true; psi.RedirectStandardInput = true; psi.RedirectStandardOutput = true; psi.RedirectStandardError = true;
                    psi.StandardOutputEncoding = Encoding.UTF8; psi.StandardErrorEncoding = Encoding.UTF8;
                    if (command != "list" && (!Path.IsPathRooted(codex) || !File.Exists(codex))) return new ConnectionReply { code = "client_error" };
                    if (!string.IsNullOrEmpty(codex)) psi.EnvironmentVariables["CODEX_CLI_PATH"] = codex;
                    if (command == "query") psi.EnvironmentVariables["CODEX_QUOTA_CREDIT_DETAILS"] = includeCreditDetails ? "1" : "0";
                    using (Process p = new Process())
                    {
                        p.StartInfo = psi; p.ErrorDataReceived += delegate { };
                        p.OutputDataReceived += delegate(object sender, DataReceivedEventArgs e)
                        {
                            if (e.Data == null) return;
                            lock (outputLock)
                            {
                                if (++lines > 8 || e.Data.Length > 32768) { bad = true; return; }
                                try { var r = new JavaScriptSerializer().Deserialize<ConnectionReply>(e.Data); if (r == null || r.@event != "result" || (command != "list" && r.slot != slot) || result != null) bad = true; else result = r; }
                                catch { bad = true; }
                            }
                        };
                        lock (processGate)
                        {
                            if (stopped) return new ConnectionReply { code = "cancelled" };
                            try { p.Start(); }
                            catch (System.ComponentModel.Win32Exception) { return new ConnectionReply { code = "node_start_failed" }; }
                            active = p;
                        }
                        p.BeginOutputReadLine(); p.BeginErrorReadLine();
                        Stopwatch sw = Stopwatch.StartNew(); bool cancelled = false; long cancelAt = 0;
                        while (!p.WaitForExit(200))
                        {
                            if (!cancelled && (stopped || sw.ElapsedMilliseconds >= 90000)) { cancelled = true; cancelAt = sw.ElapsedMilliseconds; CancelProcess(); }
                            if (cancelled && sw.ElapsedMilliseconds - cancelAt >= 12000)
                            {
                                try { var kill = new ProcessStartInfo("taskkill.exe", "/PID " + p.Id + " /T /F"); kill.UseShellExecute = false; kill.CreateNoWindow = true; using (Process k = Process.Start(kill)) k.WaitForExit(5000); } catch { try { p.Kill(); } catch { } }
                                return new ConnectionReply { code = stopped ? "cancelled" : "timeout" };
                            }
                        }
                        p.WaitForExit();
                        lock (outputLock) { if (cancelled) return new ConnectionReply { code = stopped ? "cancelled" : "timeout" }; if (bad || result == null || p.ExitCode != 0) return new ConnectionReply { code = "client_error" }; return result; }
                    }
                }
                catch { return new ConnectionReply { code = "client_error" }; }
                finally { lock (processGate) active = null; }
            }
        }
        private void CancelProcess()
        {
            lock (processGate) if (active != null) try { active.StandardInput.WriteLine("{\"action\":\"cancel\"}"); active.StandardInput.Flush(); } catch { }
        }
        internal void ReloadBindings()
        {
            lock (gate) { listNeeded = true; generation++; }
            wake.Set();
        }
        internal void Pump()
        {
            while (true)
            {
                WorkResult work;
                lock (gate) { if (results.Count == 0) break; work = results.Dequeue(); if (work.generation != generation) continue; }
                if (work.command == "list") ApplyList(work.reply); else ApplyQuery(work);
                UpdateOverview();
            }
            if (Managed && SyncGroup()) SaveStatus();
        }
        private void ApplyList(ConnectionReply result)
        {
            if (result == null || !result.ok || result.accounts == null || result.accounts.Length > 5) { host.ReportMultiDiscoveryFailure(); return; }
            var incoming = new Dictionary<string, ConnectedSlot>();
            foreach (ConnectedSlot a in result.accounts)
            {
                if (a == null || !DisplayPolicy.ValidSlot(a.slot) || incoming.ContainsKey(a.slot) ||
                    (a.connected && (a.identity == null || string.IsNullOrEmpty(a.identity.userId) || string.IsNullOrEmpty(a.identity.accountId))))
                { host.ReportMultiDiscoveryFailure(); return; }
                incoming.Add(a.slot, a);
            }
            // Keep the original tray icon available while removing or replacing icons.
            var before = new HashSet<string>(states.Keys);
            bool replacing = false;
            foreach (KeyValuePair<string, State> pair in states)
                if (!incoming.ContainsKey(pair.Key) || !incoming[pair.Key].connected || !SameIdentity(pair.Value.account.identity, incoming[pair.Key].identity)) replacing = true;
            if (replacing && Managed) { StopGroup(); host.SetMultiMode(false); Managed = false; }
            lock (gate)
            {
                var remove = new List<string>();
                foreach (KeyValuePair<string, State> pair in states)
                    if (!incoming.ContainsKey(pair.Key) || !incoming[pair.Key].connected || !SameIdentity(pair.Value.account.identity, incoming[pair.Key].identity)) remove.Add(pair.Key);
                foreach (string key in remove) { if (states[key].icon != null) { states[key].icon.Visible = false; states[key].icon.Dispose(); } states.Remove(key); dailyPace.ResetSlot(key); resetCreditsTracker.ResetSlot(key); }
                foreach (ConnectedSlot a in incoming.Values)
                {
                    if (!a.connected || a.identity == null || string.IsNullOrEmpty(a.identity.userId) || string.IsNullOrEmpty(a.identity.accountId)) continue;
                    if (!states.ContainsKey(a.slot)) states.Add(a.slot, new State());
                    states[a.slot].account = a;
                    if (!canQuery) states[a.slot].error = L.T("未识别到 Codex");
                }
            }
            if (states.Count == 0)
            {
                StopGroup();
                if (Managed) { host.SetMultiMode(false); Managed = false; }
                host.MultiDiscoveryComplete();
                desktopSource = null; SaveStatus(); return;
            }
            List<DisplayAccount> accounts = Accounts();
            if (!DisplayPolicy.IsValid(accounts, prefs))
            {
                var chosen = new List<string>();
                if (prefs != null && prefs.traySlots != null) foreach (string slot in prefs.traySlots) if (states.ContainsKey(slot) && !chosen.Contains(slot)) chosen.Add(slot);
                if (chosen.Count == 0) foreach (DisplayAccount a in accounts) chosen.Add(a.Slot);
                string desktop = prefs != null && (prefs.desktopSlot == "auto" || states.ContainsKey(prefs.desktopSlot ?? "")) ? prefs.desktopSlot : "auto";
                prefs = new DisplayPreferences(chosen.ToArray(), desktop);
            }
            // 新绑定的账号直接加入托盘显示，不需要重启后再去设置里勾选。
            if (prefs != null && prefs.traySlots != null && prefs.traySlots.Length < 5)
            {
                var shown = new List<string>(prefs.traySlots);
                foreach (DisplayAccount account in accounts)
                    if (!before.Contains(account.Slot) && !shown.Contains(account.Slot) && shown.Count < 5) shown.Add(account.Slot);
                if (shown.Count != prefs.traySlots.Length) prefs = new DisplayPreferences(shown.ToArray(), prefs.desktopSlot);
            }
            ApplyPreferences();
            lock (gate) refreshAll = true;
            wake.Set();
        }
        private void ApplyQuery(WorkResult work)
        {
            State s;
            if (!states.TryGetValue(work.slot, out s) || !SameIdentity(s.account.identity, work.expected)) return;
            ConnectionReply r = work.reply;
            if (r != null && r.ok && (!SameIdentity(work.expected, r.identity) || r.slot != work.slot)) r = new ConnectionReply { code = "identity_mismatch" };
            if (r != null && r.ok && !ValidWindow(r.primary) && !ValidWindow(r.secondary)) r = new ConnectionReply { code = "no_quota_data" };
            if (r == null || !r.ok) s.error = SafeCode(r == null ? null : r.code);
            else
            {
                if (!ValidWindow(r.primary)) r.primary = null;
                if (!ValidWindow(r.secondary)) r.secondary = null;
                r = TrayContext.NormalizeMultiReply(r);
                s.reply = r; s.lastSuccess = DateTime.Now; s.error = null;
                MergeResetCredits(s, r, work.creditDetailsRequested);
                string updatedPlan = SafePlan(r.identity.plan);
                if (updatedPlan != "unknown") s.account.plan = updatedPlan;
                s.account.emailMask = Mask(r.identity.emailMask);
                Evaluate(s, work.creditDetailsRequested);
            }
            UpdateIcon(s);
            SyncDesktop();
            SyncGroup();
            SaveStatus();
        }
        private void MergeResetCredits(State s, ConnectionReply r, bool creditDetailsRequested)
        {
            if (r == null || r.resetCredits == null)
            {
                if (s.cachedResetCredits != null && r != null) r.resetCredits = s.cachedResetCredits;
                return;
            }
            if (r.resetCredits.hasDetails)
            {
                s.cachedResetCredits = r.resetCredits;
                s.lastCreditDetailUtc = DateTime.UtcNow;
                s.lastCreditCount = r.resetCredits.availableCount;
                s.forceCreditDetail = false;
                return;
            }
            bool countChanged = s.lastCreditCount != r.resetCredits.availableCount;
            s.lastCreditCount = r.resetCredits.availableCount;
            if (r.resetCredits.availableCount == 0)
            {
                s.cachedResetCredits = r.resetCredits;
                s.forceCreditDetail = false;
                if (creditDetailsRequested) s.lastCreditDetailUtc = DateTime.UtcNow;
                return;
            }
            if (countChanged && !creditDetailsRequested)
            {
                s.forceCreditDetail = true;
                lock (gate) if (refreshSlot == null) refreshSlot = s.account.slot;
                wake.Set();
            }
            else if (s.cachedResetCredits != null && s.cachedResetCredits.availableCount == r.resetCredits.availableCount)
            {
                r.resetCredits = s.cachedResetCredits;
            }
            if (creditDetailsRequested)
            {
                s.lastCreditDetailUtc = DateTime.UtcNow;
                s.forceCreditDetail = false;
            }
        }
        private void SyncDesktop()
        {
            string previous = desktopSource;
            desktopSource = DisplayPolicy.ChooseDesktop(Accounts(), prefs);
            State source;
            if (desktopSource != null && states.TryGetValue(desktopSource, out source) && source.reply != null)
                host.SyncMultiDesktop(source.reply);
            else if (previous != null && previous != desktopSource)
                host.SyncMultiDesktop(null);
        }
        private static WindowInfo LongQuotaWindow(ConnectionReply reply)
        {
            if (reply == null) return null;
            if (reply.secondary != null && QuotaWindow.IsLong(reply.secondary.windowMins)) return reply.secondary;
            if (reply.primary != null && QuotaWindow.IsLong(reply.primary.windowMins)) return reply.primary;
            return null;
        }
        private void Evaluate(State s, bool creditDetailsRequested)
        {
            WindowInfo longWin = LongQuotaWindow(s.reply);
            if (longWin != null)
            {
                DailyPaceDecision pace = dailyPace.Evaluate(s.account.slot, settings.DailyPaceFor(s.account.slot),
                    longWin.used, longWin.resetsAt, DateTimeOffset.UtcNow);
                if (pace.Triggered)
                    host.ShowMultiToast(s.account.slot, LabelFor(s), NotifyRules.DailyPaceBody(), TrayContext.NoSound, AccountMask(s), true);
            }
            ResetCreditDecision rc = resetCreditsTracker.Evaluate(s.account.slot, settings.ResetCreditFor(s.account.slot),
                s.reply.resetCredits, DateTimeOffset.UtcNow, creditDetailsRequested);
            if (rc.NewCreditTriggered)
                host.ShowMultiToast(s.account.slot, LabelFor(s), NotifyRules.ResetCreditNewBody(rc.AvailableCount, rc.SoonestExpiresAt), TrayContext.RecoverySound, AccountMask(s), false);
            else if (rc.ExpiringTriggered)
                host.ShowMultiToast(s.account.slot, LabelFor(s), NotifyRules.ResetCreditExpiringBody(rc.AvailableCount, rc.ExpiringSoonestAt), "notify.wav", AccountMask(s), false);
            int level = settings.LevelFor(s.account.slot);
            if (level < 0) { s.tier = null; s.fixedWindow = null; s.level = level; return; }
            bool single = s.reply.primary == null || QuotaWindow.IsLong(s.reply.primary.windowMins);
            if (s.tier == null || s.level != level || s.single != single)
            {
                s.tier = new NotifyWindow { thresholds = NotifyRules.Tier(level) };
                s.fixedWindow = new NotifyWindow { thresholds = NotifyRules.LongTier(level) };
                s.level = level; s.single = single;
            }
            if (single) Notify(s, s.fixedWindow, s.reply.secondary ?? s.reply.primary, true);
            else { Notify(s, s.tier, s.reply.primary, false); Notify(s, s.fixedWindow, s.reply.secondary, true); }
        }
        private void Notify(State s, NotifyWindow n, WindowInfo w, bool longWindow)
        {
            if (w == null) return;
            int t = n.Fire(w.used, w.resetsAt);
            int days = longWindow ? QuotaWindow.Days(w.windowMins) : 0;
            string window = longWindow ? L.F("{0} 天", days) : L.T("5 小时");
            if (n.Recovered && NotifyRules.CanShowRecovery(s.account.plan, days))
                host.ShowMultiToast(s.account.slot, LabelFor(s), NotifyRules.RecoveryBody(days), TrayContext.RecoverySound, AccountMask(s), true);
            if (n.Exhausted) host.ShowMultiToast(s.account.slot, LabelFor(s), L.F("{0}额度已耗尽", window), longWindow ? "notify-7d-empty.wav" : "notify-5h-empty.wav", AccountMask(s));
            else if (t > 0) host.ShowMultiToast(s.account.slot, LabelFor(s), L.F("{0}剩余 {1}%", window, Math.Max(0, 100 - (int)Math.Round(w.used))) + "\n" + TrayContext.MultiTooltipCountdown(w.resetsAt), "notify.wav", AccountMask(s));
        }
        private void ApplyPreferences()
        {
            if (!DisplayPolicy.IsValid(Accounts(), prefs)) return;
            // Build all new icons before hiding the host icon.
            foreach (State s in states.Values)
            {
                if (s.icon == null)
                {
                    State captured = s;
                    var icon = new NotifyIcon(); icon.ContextMenuStrip = menu;
                    ((UiTrayMenu)menu).Track(icon);
                    icon.Icon = TrayContext.MakeMultiIcon(s.reply);
                    s.hover = new TrayIconHover(icon, (UiTrayMenu)menu);
                    s.hover.SetFallbackTip(ShortText(LabelFor(s)));
                    icon.MouseDown += delegate { selected = captured.account.slot; host.HideQuotaTip(); host.PrepareMultiMenu(selected, LabelFor(captured)); };
                    icon.MouseMove += delegate
                    {
                        captured.hover.Reassert();
                        if (captured.hover.Enabled && !host.ShowQuotaTip(icon, LabelFor(captured), captured.reply, captured.error, captured.lastSuccess))
                            captured.hover.Disable();
                    };
                    icon.DoubleClick += delegate { selected = captured.account.slot; RefreshSelected(); };
                    s.icon = icon;
                    s.iconKey = null;
                }
            }
            foreach (State s in states.Values) if (Array.IndexOf(prefs.traySlots, s.account.slot) >= 0) s.hover.SetVisible(true);
            if (!Managed) { host.SetMultiMode(true); Managed = true; }
            foreach (State s in states.Values) if (Array.IndexOf(prefs.traySlots, s.account.slot) < 0) s.hover.SetVisible(false);
            if (selected == null || !states.ContainsKey(selected)) { var sorted = new List<string>(states.Keys); sorted.Sort(StringComparer.Ordinal); selected = sorted[0]; }
            foreach (State s in states.Values) UpdateIcon(s);
            SyncDesktop();
            SyncGroup();
            SaveStatus();
        }
        private string TooltipFor(State s)
        {
            return BuildTooltip(s.account.slot, s.account.plan ?? (s.account.identity == null ? null : s.account.identity.plan),
                s.account.emailMask ?? (s.account.identity == null ? null : s.account.identity.emailMask), s.reply, s.error, s.lastSuccess);
        }
        // 固定分体托盘；不创建任务栏宿主、定位探测或组合窗口。
        private bool SyncGroup() { return false; }
        internal void RunGroupUiTest() { }
        private void StopGroup() { groupActive = false; groupError = null; }
        private static string ShortText(string value) { return value.Length <= 63 ? value : value.Substring(0, 63); }
        private void UpdateIcon(State s)
        {
            if (s.icon == null) return;
            if (s.reply != null && s.error == null)
            {
                string key = IconUsage(s.reply.primary) + "/" + IconUsage(s.reply.secondary);
                if (key != s.iconKey)
                {
                    Icon old = s.icon.Icon;
                    s.hover.SetIcon(TrayContext.MakeMultiIcon(s.reply));
                    s.iconKey = key;
                    if (old != null && old != SystemIcons.Application) old.Dispose();
                }
            }
            string tooltip = BuildNativeTooltip(s.account.slot, s.account.plan ?? (s.account.identity == null ? null : s.account.identity.plan),
                s.account.emailMask ?? (s.account.identity == null ? null : s.account.identity.emailMask), s.reply, s.error, s.lastSuccess);
            // Keep NIF_TIP absent while custom hover owns the icon; store fallback text internally.
            s.hover.SetFallbackTip(tooltip);
            if (!s.hover.Enabled && tooltip != s.icon.Text) s.icon.Text = tooltip;
            if (s.hover.Enabled && !host.RefreshQuotaTip(s.icon, LabelFor(s), s.reply, s.error, s.lastSuccess))
                s.hover.Disable();
        }

        private static string IconUsage(WindowInfo quota)
        {
            return quota == null ? "-" : QuotaWindow.DisplayUsed(quota.used).ToString();
        }

        internal static string BuildTooltip(string slot, string plan, string mask, ConnectionReply reply, string error, DateTime lastSuccess)
        {
            string header = L.F("账号 {0}", SlotNumber(slot)) + "  |  " + PlanTitle(plan);
            string body = "";
            if (error != null && reply != null) body = L.T("上次额度");
            if (reply != null)
            {
                foreach (WindowInfo w in new WindowInfo[] { reply.primary, reply.secondary })
                {
                    if (w == null) continue;
                    if (body.Length > 0) body += "\n";
                    int remain = QuotaWindow.Remaining(w.used);
                    body += (QuotaWindow.IsLong(w.windowMins) ? L.F("{0} 天 剩余 {1}%", QuotaWindow.Days(w.windowMins), remain) : L.F("5 小时 剩余 {0}%", remain)) + "｜" + TrayContext.MultiTooltipCountdown(w.resetsAt);
                }
            }
            if (error != null) body += (body.Length == 0 ? "" : "\n") + L.T("更新失败") + "\n" + L.T("最后成功") + " " + (lastSuccess == DateTime.MinValue ? L.T("暂无") : lastSuccess.ToString("MM-dd HH:mm"));
            string suffix = body.Length == 0 ? "" : "\n" + body;
            return header + " " + Mask(mask) + suffix;
        }
        internal static string BuildNativeTooltip(string slot, string plan, string mask, ConnectionReply reply, string error, DateTime lastSuccess)
        {
            string full = BuildTooltip(slot, plan, mask, reply, error, lastSuccess);
            if (full.Length <= 63) return full;
            string account = L.Lang == "en" ? "Acct " + SlotNumber(slot) : L.F("账号 {0}", SlotNumber(slot)).Replace(" ", "");
            if (error == null && reply != null)
            {
                // 分体NotifyIcon限63字；先压缩时间的单位间空格，保留两条恢复时间。
                string timed = account + "|" + PlanTitle(plan) + "|" + Mask(mask);
                foreach (WindowInfo w in new WindowInfo[] { reply.primary, reply.secondary })
                {
                    if (w == null) continue;
                    int remain = QuotaWindow.Remaining(w.used);
                    bool weekly = QuotaWindow.IsLong(w.windowMins);
                    string quota = weekly ? L.F("{0} 天 剩余 {1}%", QuotaWindow.Days(w.windowMins), remain) : L.F("5 小时 剩余 {0}%", remain);
                    if (L.Lang == "en") quota = (weekly ? QuotaWindow.Days(w.windowMins) + "d" : "5h") + " left " + remain + "%";
                    timed += "\n" + quota + "｜" + TrayContext.MultiTooltipCountdown(w.resetsAt).Replace(" ", "");
                }
                if (timed.Length > 63 && L.Lang == "zh") timed = timed.Replace("剩余 ", "剩余");
                if (timed.Length > 63 && L.Lang == "en") timed = timed.Replace(" left ", " ");
                if (timed.Length > 63 && L.Lang == "ja") timed = timed.Replace(" ", "");
                if (timed.Length <= 63) return timed;
            }
            // Extreme identity lengths still need the bounded native fallback below.
            // Keep the identity there too, shortening quota detail rather than removing the account.
            string compact = account + "|" + PlanTitle(plan) + "|" + Mask(mask);
            if (error != null) compact += "\n" + L.T("更新失败");
            else if (reply != null)
            {
                foreach (WindowInfo w in new WindowInfo[] { reply.primary, reply.secondary })
                {
                    if (w == null) continue;
                    int remain = QuotaWindow.Remaining(w.used);
                    compact += "\n" + (QuotaWindow.IsLong(w.windowMins) ? L.F("{0} 天 剩余 {1}%", QuotaWindow.Days(w.windowMins), remain) : L.F("5 小时 剩余 {0}%", remain));
                }
            }
            return ShortText(compact);
        }
        private void SaveStatus()
        {
            try
            {
                var list = new List<object>();
                int visibleCount = 0;
                foreach (State s in states.Values)
                {
                    bool visible = groupActive ? prefs != null && Array.IndexOf(prefs.traySlots, s.account.slot) >= 0 : s.icon != null && s.icon.Visible;
                    if (visible) visibleCount++;
                    list.Add(new { slot = s.account.slot, visible = visible, plan = SafePlan(s.account.plan), primary = s.reply == null ? null : s.reply.primary, secondary = s.reply == null ? null : s.reply.secondary, error = s.error, lastSuccess = s.lastSuccess == DateTime.MinValue ? null : s.lastSuccess.ToUniversalTime().ToString("o"), tooltip = TooltipFor(s), fullTooltip = TooltipFor(s) });
                }
                File.WriteAllText(Path.Combine(root, "multi-account-status.json"), new JavaScriptSerializer().Serialize(new { desktopSource = desktopSource, desktopPreference = prefs == null ? null : prefs.desktopSlot, visibleIconCount = visibleCount, visibleAccountCount = visibleCount, grouped = false, presentation = "separate-tray", groupError = (string)null, groupWindow = 0, accounts = list }), new UTF8Encoding(false));
            }
            catch { }
        }
        internal void RefreshAccount(string slot) { if (!DisplayPolicy.ValidSlot(slot)) return; lock (gate) refreshSlot = slot; wake.Set(); }
        internal void RefreshSelected() { if (selected != null) RefreshAccount(selected); }
        internal void ShowBalloon(string title, string text)
        {
            State state;
            if (selected != null && states.TryGetValue(selected, out state) && state.icon != null && state.icon.Visible)
            {
                state.icon.ShowBalloonTip(3000, title, text, ToolTipIcon.Info);
                return;
            }
            foreach (State item in states.Values)
                if (item.icon != null && item.icon.Visible) { item.icon.ShowBalloonTip(3000, title, text, ToolTipIcon.Info); return; }
        }
        private List<AccountOverviewRow> OverviewRows()
        {
            var rows = new List<AccountOverviewRow>();
            var keys = new List<string>(states.Keys); keys.Sort(StringComparer.Ordinal);
            foreach (string key in keys)
            {
                State s = states[key];
                rows.Add(new AccountOverviewRow { Slot = key, Label = LabelFor(s), Primary = s.reply == null ? null : s.reply.primary,
                    Secondary = s.reply == null ? null : s.reply.secondary, Failed = s.error != null && s.error != "not_checked", LastSuccess = s.lastSuccess });
            }
            return rows;
        }
        private void UpdateOverview() { if (overviewForm != null && !overviewForm.IsDisposed) overviewForm.SetRows(OverviewRows()); host.RefreshSettingsAccounts(); }
        internal void ShowAllDetails()
        {
            host.ShowSettingsPage("accounts");
        }
        internal void ShowSelectedDetail() { ShowAllDetails(); }
        internal void CreateDesktopShortcut()
        {
            State s;
            if (desktopSource != null && states.TryGetValue(desktopSource, out s) && s.reply != null && s.error == null) host.EnsureMultiShortcut(s.reply);
            else ShowBalloon(L.T("额度显示器"), L.T("所选账号暂未取得最新额度。"));
        }
        internal void ApplyLanguage() { foreach (State s in states.Values) UpdateIcon(s); UpdateOverview(); }
        internal void ShowSettings() { host.ShowSettingsPage("display"); }
        internal List<SettingsAccount> SettingsAccounts()
        {
            var rows = new List<SettingsAccount>();
            var order = new List<string>(states.Keys); order.Sort(StringComparer.Ordinal);
            foreach (string slot in order)
            {
                State s = states[slot];
                string full = TooltipFor(s);
                int line = full.IndexOf('\n');
                string plan=PlanTitle(s.account.plan ?? (s.account.identity == null ? null : s.account.identity.plan));
                string email=AccountMask(s);
                rows.Add(new SettingsAccount { Primary = s.reply == null ? null : s.reply.primary, Secondary = s.reply == null ? null : s.reply.secondary, ResetCredits = s.reply == null ? s.cachedResetCredits : (s.reply.resetCredits ?? s.cachedResetCredits), Failed = s.error != null && s.error != "not_checked", Slot = slot, DisplayNumber = SlotNumber(slot), Plan = plan, EmailMask = email, Label = LabelFor(s), Quota = line < 0 ? L.T("正在读取额度…") : full.Substring(line + 1), Visible = prefs != null && Array.IndexOf(prefs.traySlots, slot) >= 0, NotifyLevel = settings.LevelFor(slot), DailyPace = settings.DailyPaceFor(slot), ResetCredit = settings.ResetCreditFor(slot) });
            }
            return rows;
        }
        internal string DesktopPreference { get { return prefs == null ? "auto" : prefs.desktopSlot; } }
        internal bool SaveDisplay(string[] slots, string desktop)
        {
            var next = new DisplayPreferences(slots, desktop);
            if (!DisplayPolicy.IsValid(Accounts(), next)) return false;
            try
            {
                string target = Path.Combine(root, "display-settings.json");
                string temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
                File.WriteAllText(temporary, new JavaScriptSerializer().Serialize(next), new UTF8Encoding(false));
                if (File.Exists(target)) File.Replace(temporary, target, null); else File.Move(temporary, target);
            }
            catch { return false; }
            prefs = next; ApplyPreferences(); return true;
        }
        internal void RefreshAll() { lock (gate) { refreshAll = true; if (states.Count == 0) listNeeded = true; } wake.Set(); }
        // 右键“刷新”必须连同绑定列表一起重读，否则新绑定的账号要重启才出现。
        internal void ReloadAndRefresh() { lock (gate) { listNeeded = true; refreshAll = true; generation++; } wake.Set(); }
        internal void ResetReminder(string slot)
        {
            State s;
            if (states.TryGetValue(slot, out s)) { s.tier = null; s.fixedWindow = null; }
        }
        public void Dispose()
        {
            if (stopped) return;
            stopped = true; CancelProcess(); wake.Set(); worker.Join(18000);
            if (settingsForm != null && !settingsForm.IsDisposed) settingsForm.Close();
            if (overviewForm != null && !overviewForm.IsDisposed) overviewForm.Close();
            StopGroup();
            foreach (State s in states.Values) if (s.icon != null) { s.icon.Visible = false; s.icon.Dispose(); }
            if (!worker.IsAlive) wake.Close();
        }
    }
    internal static class NativeGroupDiagnostics
    {
        private delegate bool EnumProc(IntPtr h, IntPtr state);
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc callback, IntPtr state);
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr h);
        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)] private static extern int GetClassName(IntPtr h, StringBuilder text, int length);
        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)] private static extern int GetWindowText(IntPtr h, StringBuilder text, int length);
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        internal static long Foreground() { return GetForegroundWindow().ToInt64(); }
        internal static string[] Tooltips()
        {
            var texts = new List<string>(); uint own = (uint)Process.GetCurrentProcess().Id;
            EnumWindows(delegate(IntPtr h, IntPtr unused) {
                uint pid; GetWindowThreadProcessId(h, out pid);
                if (pid != own || !IsWindowVisible(h)) return true;
                var cls = new StringBuilder(256); GetClassName(h, cls, cls.Capacity);
                if (cls.ToString() != "tooltips_class32") return true;
                var text = new StringBuilder(4096); GetWindowText(h, text, text.Capacity); texts.Add(text.ToString()); return true;
            }, IntPtr.Zero);
            return texts.ToArray();
        }
    }
    internal sealed class AccountOverviewRow
    {
        internal string Slot, Label;
        internal WindowInfo Primary, Secondary;
        internal bool Failed;
        internal DateTime LastSuccess;
    }

    internal sealed class AccountOverviewForm : Form
    {
        private readonly FlowLayoutPanel list;
        private readonly Button refresh, close;
        private readonly Color cardColor;
        internal int RowCount { get { return list.Controls.Count; } }
        internal Button RefreshButton { get { return refresh; } }

        internal AccountOverviewForm(IList<AccountOverviewRow> rows, Action refreshAll)
        {
            Text = L.T("额度详情"); Font = UiTheme.UiFont(SystemFonts.MessageBoxFont.SizeInPoints); AutoScaleMode = AutoScaleMode.Font;
            StartPosition = FormStartPosition.CenterScreen; ShowIcon = false; MinimizeBox = false;
            bool dark = TrayContext.IsDarkMode();
            BackColor = dark ? Color.FromArgb(32, 32, 32) : SystemColors.Window;
            ForeColor = dark ? Color.FromArgb(235, 235, 235) : SystemColors.ControlText;
            cardColor = dark ? Color.FromArgb(48, 48, 52) : SystemColors.Control;
            if (dark) HandleCreated += delegate { TrayContext.SetDarkTitleBar(Handle); };
            var footer = new FlowLayoutPanel(); footer.Dock = DockStyle.Bottom; footer.Height = 48; footer.FlowDirection = FlowDirection.RightToLeft; footer.Padding = new Padding(8, 5, 8, 5); footer.WrapContents = false;
            close = new Button(); close.Text = L.T("关闭"); close.AutoSize = true; close.Click += delegate { Close(); }; footer.Controls.Add(close);
            refresh = new Button(); refresh.Text = L.T("刷新"); refresh.AutoSize = true; refresh.Click += delegate { if (refreshAll != null) refreshAll(); }; footer.Controls.Add(refresh);
            if (dark) foreach (Control c in footer.Controls) { c.BackColor = cardColor; c.ForeColor = ForeColor; }
            list = new FlowLayoutPanel(); list.Dock = DockStyle.Fill; list.AutoScroll = true; list.FlowDirection = FlowDirection.TopDown; list.WrapContents = false; list.Padding = new Padding(8);
            Controls.Add(list); Controls.Add(footer);
            list.ClientSizeChanged += delegate { ResizeCards(); };
            int screenHeight = Screen.PrimaryScreen.WorkingArea.Height;
            ClientSize = new Size(Math.Min(560, Screen.PrimaryScreen.WorkingArea.Width - 40), Math.Min(95 + rows.Count * 112, Math.Max(180, screenHeight - 100)));
            MinimumSize = new Size(300, 180);
            SetRows(rows);
            int contentHeight = list.Padding.Vertical + footer.Height + 8;
            foreach (Control card in list.Controls) contentHeight += card.Height + card.Margin.Vertical;
            ClientSize = new Size(ClientSize.Width, Math.Min(Math.Max(150, contentHeight), Math.Max(150, screenHeight - 100)));
        }
        private void ResizeCards()
        {
            int width = Math.Max(100, list.ClientSize.Width - list.Padding.Horizontal - SystemInformation.VerticalScrollBarWidth - 8);
            foreach (Control c in list.Controls)
            {
                c.Width = width;
                foreach (Control line in c.Controls) line.MaximumSize = new Size(Math.Max(40, width - 32), 0);
                c.Height = c.GetPreferredSize(new Size(width, 0)).Height;
            }
        }
        private static string QuotaText(WindowInfo w)
        {
            if (w == null) return null;
            string title = QuotaWindow.IsLong(w.windowMins) ? L.F("{0} 天额度", QuotaWindow.Days(w.windowMins)) : L.T("5 小时额度");
            string reset = w.resetsAt <= 0 ? L.T("暂无") : new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(w.resetsAt).ToLocalTime().ToString("yyyy-MM-dd HH:mm");
            return title + " · " + L.F("剩余 {0}%", QuotaWindow.Remaining(w.used)) + " · " + L.F("恢复：{0}", reset);
        }
        private void AddLine(TableLayoutPanel card, string text)
        {
            var label = new Label(); label.AutoSize = true; label.Margin = new Padding(8, 3, 8, 3); label.Text = text; label.ForeColor = ForeColor;
            card.Controls.Add(label);
        }
        internal void SetRows(IList<AccountOverviewRow> rows)
        {
            Text = L.T("额度详情"); refresh.Text = L.T("刷新"); close.Text = L.T("关闭");
            Point scrollPosition = list.AutoScrollPosition;
            list.SuspendLayout();
            while (list.Controls.Count > 0) { Control c = list.Controls[0]; list.Controls.RemoveAt(0); c.Dispose(); }
            foreach (AccountOverviewRow row in rows)
            {
                var card = new TableLayoutPanel(); card.AutoSize = false; card.ColumnCount = 1; card.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); card.BackColor = cardColor; card.Margin = new Padding(0, 0, 0, 8); card.Padding = new Padding(4);
                AddLine(card, row.Label);
                string first = QuotaText(row.Primary), second = QuotaText(row.Secondary);
                if (first != null) AddLine(card, first);
                if (second != null) AddLine(card, second);
                if (first == null && second == null) AddLine(card, L.T("暂无数据"));
                if (row.Failed) AddLine(card, L.T("更新失败") + " · " + L.T("最后成功") + " " + (row.LastSuccess == DateTime.MinValue ? L.T("暂无") : row.LastSuccess.ToString("yyyy-MM-dd HH:mm")));
                list.Controls.Add(card);
            }
            ResizeCards(); list.ResumeLayout(true);
            list.AutoScrollPosition = new Point(-scrollPosition.X, -scrollPosition.Y);
        }
    }
#endif
}
