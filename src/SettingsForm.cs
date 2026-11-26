using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace QuotaTrayApp
{
    internal sealed class SettingsAccount { internal string Slot, Label, Plan, EmailMask, Quota; internal int DisplayNumber; internal WindowInfo Primary, Secondary; internal ResetCreditsInfo ResetCredits; internal bool Failed; internal bool Visible; internal int NotifyLevel; internal bool DailyPace = true; internal bool ResetCredit = true; }
    internal sealed class SettingsSnapshot
    {
        internal List<SettingsAccount> Accounts;
        internal string DesktopSlot, Language, Theme, AccountReadError;
        internal int DefaultNotifyLevel;
        internal bool NotifySound = true;
        internal int NotifyVolume = AppSettings.DefaultNotifyVolume;
        internal bool DefaultDailyPace = true;
        internal bool DefaultResetCredit = true;
        internal bool AutoUpdate = true;
        internal bool AutoStart, AccountsLoading, CodexMissing;
    }
    internal sealed class SettingsActions
    {
        internal Func<SettingsSnapshot> Read;
        internal Action ConnectAccounts, RefreshAll, LocateCodex, CreateShortcut, Exit;
        internal Func<string[], string, bool> SaveDisplay;
        internal Action<string, int> SetAccountLevel;
        internal Func<int, bool> ApplyGlobalLevel;
        internal Func<bool, bool> ApplyGlobalDailyPace;
        internal Func<string, bool, bool> SetAccountDailyPace;
        internal Func<bool, bool> ApplyGlobalResetCredit;
        internal Func<string, bool, bool> SetAccountResetCredit;
        internal Func<bool, bool> SetNotifySound;
        internal Func<int, bool> SetNotifyVolume;
        internal Action<int> TestNotifyVolume;
        internal Action<string> SetLanguage, SetTheme;
        internal Func<bool, bool> SetAutoStart, SetAutoUpdate;
        internal Func<UpdateStatus> ReadUpdate;
        internal Action CheckUpdate, ApplyUpdate;
        internal Func<Version> ReadVersion;
    }

    internal sealed class QuotaSettingsForm : UiWindow
    {
        private readonly SettingsActions actions;
        private readonly UiFlowPanel sidebar, content;
        private readonly UiScrollBar scrollBar;
        private readonly WheelFilter wheelFilter;
        private readonly System.Windows.Forms.Timer scrollTimer = new System.Windows.Forms.Timer { Interval = 15 };
        private readonly Dictionary<Control, int> baseChildTops = new Dictionary<Control, int>();
        private double scrollTarget, scrollCurrent, overscroll;
        private int maxScroll, totalContentHeight, lastAppliedOffset = int.MinValue;
        private bool applyingScroll;
        [DllImport("user32.dll")] private static extern IntPtr BeginDeferWindowPos(int numWindows);
        [DllImport("user32.dll")] private static extern IntPtr DeferWindowPos(IntPtr winPosInfo, IntPtr hwnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] private static extern bool EndDeferWindowPos(IntPtr winPosInfo);
        [DllImport("user32.dll")] private static extern bool RedrawWindow(IntPtr hwnd, IntPtr rectUpdate, IntPtr regionUpdate, uint flags);
        private readonly RadioButton[] navigation = new RadioButton[4];
        private bool updatingNavigation, resizingCards;
        private readonly System.Windows.Forms.Timer themeTimer = new System.Windows.Forms.Timer();
        private readonly System.Windows.Forms.Timer currentNoticeTimer = new System.Windows.Forms.Timer();
        private readonly ToolTip tips = new ToolTip();
        private Label versionLabel;
        private UpdateVersionRange versionRange;
        private Button updateButton, checkUpdateButton;
        private Label updateMessage;
        private UpdateStatus updateStatus;
        private bool cardLayoutQueued;
        private int? pendingGlobalLevel;
        private readonly Dictionary<string, FlowLayoutPanel> accountQuotaRows = new Dictionary<string, FlowLayoutPanel>();
        private string page;
        private SettingsSnapshot snapshot;
        private readonly List<CheckBox> displayToggles = new List<CheckBox>();
        private UiComboBox desktop;
        private Label error;
        private readonly List<string> desktopSlots = new List<string>();
        private static readonly string[] Pages = { "accounts", "display", "notifications", "general" };
        private static readonly string[] PageNames = { "账号", "显示", "额度提醒", "通用" };
        private static readonly string[] Levels = { "关闭", "低档", "中档", "高档" };
        private static readonly string[] Appearances = { "跟随系统", "浅色", "深色" };
        // Settings layout uses one baseline gap for ordinary controls.  Small
        // explanatory captions may opt into a tighter margin explicitly.
        private int LayoutGap { get { return S(10); } }
        private int CardInset { get { return S(12); } }
        private int ControlHeight { get { return S(33); } }
        private int ShortSelectWidth { get { return S(184); } }
        private int LongSelectWidth { get { return S(252); } }
        private int CardPaddingSize { get { return CardInset; } }
        private const string DailyPaceHelpKey = "按 7 天剩余额度与距重置天数动态提醒今日建议上限；也可在下方按账号单独开关。";
        private const string DailyPaceTipKey = "按 7 天剩余额度与距重置天数动态计算每日建议上限，防止提前用光。\n开启后：当日达到推荐用量时静默提醒一次（每天每账号最多一次；已耗尽或最后 24 小时不提醒）。\n关闭后：不再弹出每日推荐用量提示。";
        private const string ResetCreditHelpKey = "在「账号」页查看现有重置券，新发券或临近过期时提醒；也可在下方按账号单独开关。";
        private const string ResetCreditTipKey = "开启后：在设置「账号」页显示可用重置券张数与最近过期时间；官方发放新重置券或距离过期不足 24 小时时弹窗提醒。\n关闭后：不再弹出新券与临期提醒。";
        private static readonly string[] LevelTips = {
            "关闭额度提醒。",
            "5小时：剩25%、10%提醒；7天：剩15%、5%提醒。",
            "5小时：剩75%、25%、10%提醒；7天：剩75%、50%、25%、10%提醒。",
            "5小时：剩75%、50%、25%、10%提醒；7天：每消耗10%提醒一次，耗尽单独提醒。"
        };

        private static readonly Dictionary<string, string[]> Translations = new Dictionary<string, string[]> {
            { "外观", new string[] { "外観", "Appearance" } },
            { "账号 {0}", new string[] { "アカウント {0}", "Account {0}" } },
            { "跟随系统", new string[] { "システム設定", "System" } },
            { "浅色", new string[] { "ライト", "Light" } },
            { "深色", new string[] { "ダーク", "Dark" } },
            { "连接账号后显示实时额度", new string[] { "連携後に最新の使用量を表示", "Connect an account to see live quota" } },
            { "账号与额度", new string[] { "アカウントと使用量", "Accounts & quota" } },
            { "选择通知区域与桌面显示的账号", new string[] { "トレイとデスクトップのアカウントを選択", "Choose accounts for the tray and desktop" } },
            { "独立设置每个账号的提醒", new string[] { "アカウントごとの通知を設定", "Set alerts for each account" } },
            { "语言与系统", new string[] { "言語とシステム", "Language & system" } },
            { "程序与快捷方式", new string[] { "アプリとショートカット", "App & shortcuts" } },
            { "软件更新", new string[] { "アプリの更新", "App updates" } },
            { "检查更新", new string[] { "更新を確認", "Check for updates" } },
            { "更新", new string[] { "更新", "Update" } },
            { "正在检查更新…", new string[] { "更新を確認中…", "Checking for updates…" } },
            { "已是最新版本", new string[] { "最新バージョンです", "You’re up to date" } },
            { "可更新到 {0}", new string[] { "{0} に更新できます", "Version {0} is available" } },
            { "正在下载 {0}…", new string[] { "{0} をダウンロード中…", "Downloading {0}…" } },
            { "正在重启并应用更新…", new string[] { "再起動して更新を適用中…", "Restarting to apply the update…" } },
            { "检查失败，请重试。", new string[] { "更新を確認できませんでした。再試行してください。", "Could not check for updates. Try again." } },
            { "更新失败，请重试。", new string[] { "更新できませんでした。再試行してください。", "Could not update. Try again." } },
            { "查询失败，显示上次结果", new string[] { "更新失敗：前回の結果", "Update failed · last result" } },
            { "额度显示器设置", new string[] { "使用量表示の設定", "Quota Display Settings" } },
            { "额度显示器", new string[] { "使用量表示", "Quota Display" } },
            { "设置", new string[] { "設定", "Settings" } },
            { "账号", new string[] { "アカウント", "Accounts" } },
            { "显示", new string[] { "表示", "Display" } },
            { "额度提醒", new string[] { "使用量の通知", "Quota Alerts" } },
            { "通用", new string[] { "一般", "General" } },
            { "关闭", new string[] { "オフ", "Off" } },
            { "低档", new string[] { "低", "Low" } },
            { "中档", new string[] { "中", "Medium" } },
            { "高档", new string[] { "高", "High" } },
            { "关闭额度提醒。", new string[] { "使用量の通知をオフにします。", "Quota alerts are off." } },
            { "5小时：剩25%、10%提醒；7天：剩15%、5%提醒。", new string[] { "5時間：残り25%、10%で通知。7日間：残り15%、5%で通知。", "5-hour: alert at 25% and 10% remaining. 7-day: alert at 15% and 5% remaining." } },
            { "5小时：剩75%、25%、10%提醒；7天：剩75%、50%、25%、10%提醒。", new string[] { "5時間：残り75%、25%、10%で通知。7日間：残り75%、50%、25%、10%で通知。", "5-hour: alert at 75%, 25%, and 10% remaining. 7-day: alert at 75%, 50%, 25%, and 10% remaining." } },
            { "5小时：剩75%、50%、25%、10%提醒；7天：每消耗10%提醒一次，耗尽单独提醒。", new string[] { "5時間：残り75%、50%、25%、10%で通知。7日間：10%消費ごとに通知、使い切った時は別途通知。", "5-hour: alert at 75%, 50%, 25%, and 10% remaining. 7-day: alert for each 10% used, plus a separate alert when exhausted." } },
            { "正在读取账号…", new string[] { "アカウントを読み込み中…", "Loading accounts…" } },
            { "未绑定任何账号", new string[] { "連携済みアカウントなし", "No linked accounts" } },
            { "未识别到 Codex", new string[] { "Codex が見つかりません", "Codex not found" } },
            { "无法读取本地绑定，请手动刷新。", new string[] { "連携情報を読み取れません。手動で更新してください。", "Could not read local bindings. Refresh to retry." } },
            { "尚未绑定账号", new string[] { "アカウントがまだ接続されていません", "No accounts connected yet" } },
            { "刷新额度", new string[] { "使用量を更新", "Refresh quota" } },
            { "绑定或更改账号", new string[] { "アカウントを接続・変更", "Connect or change accounts" } },
            { "选择要显示的账号", new string[] { "表示するアカウントを選択", "Choose accounts to display" } },
            { "桌面快捷方式图标", new string[] { "デスクトップのショートカットアイコン", "Desktop shortcut icon" } },
            { "自动", new string[] { "自動", "Automatic" } },
            { "保存显示设置", new string[] { "表示設定を保存", "Save display settings" } },
            { "请至少选择一个显示账号。", new string[] { "表示するアカウントを1つ以上選択してください。", "Select at least one account to display." } },
            { "保存失败，请重试。", new string[] { "保存できませんでした。もう一度お試しください。", "Could not save. Try again." } },
            { "已保存。", new string[] { "保存しました。", "Saved." } },
            { "应用", new string[] { "適用", "Apply" } },
            { "应用后统一已绑定账号的档位，新绑定的账号也默认使用此档位。", new string[] { "適用すると連携済みのアカウントが同じレベルになり、新規アカウントにも適用されます。", "Apply sets this level for all bound accounts and makes it the default for newly bound accounts." } },
            { "默认提醒档位", new string[] { "既定の通知レベル", "Default alert level" } },
            { "推荐用量提醒", new string[] { "推奨使用量アラート", "Recommended usage alert" } },
            { DailyPaceHelpKey, new string[] {
                "7日間の残量とリセットまでの日数から1日の目安を通知します。下のアカウント別に個別の変更もできます。",
                "Dynamically alerts on a suggested daily budget from your 7-day quota; toggle per account below." } },
            { DailyPaceTipKey, new string[] {
                "7日間の残量とリセットまでの日数から1日の目安を動的に計算し、使いすぎを防ぎます。\nオン時：目安に達すると無音で1日1回通知します（残量0%や残り24時間以内は通知しません）。\nオフ時：推奨使用量の通知は表示されません。",
                "Dynamically estimates a daily budget from your remaining 7-day quota and days until reset so you do not run out early.\nWhen on: shows one silent alert per account per day once reached (skipped at 0% or in the final 24h).\nWhen off: no daily pace alerts are shown." } },
            { "重置券提醒", new string[] { "リセット券アラート", "Reset pass alerts" } },
            { ResetCreditHelpKey, new string[] {
                "「アカウント」画面でリセット券を確認し、新規付与や期限間近を通知します。下のアカウント別に個別の変更もできます。",
                "Shows available reset passes on Accounts and alerts on new passes or expiry; toggle per account below." } },
            { ResetCreditTipKey, new string[] {
                "オン時：「アカウント」画面に枚数と最短期限を表示し、新規付与時や期限24時間以内に通知します。\nオフ時：新規付与・期限間近の通知は表示されません。",
                "When on: shows pass count and earliest expiry on the Accounts page, and alerts when a new pass arrives or expires within 24h.\nWhen off: no new-pass or expiry alerts are shown." } },
            { "播放提醒声音", new string[] { "通知音を鳴らす", "Play alert sounds" } },
            { "提醒音量", new string[] { "通知音量", "Alert volume" } },
            { "试听", new string[] { "試聴", "Test" } },
            { "静音", new string[] { "消音", "Mute" } },
            { "轻声", new string[] { "小", "Soft" } },
            { "适中", new string[] { "中", "Mid" } },
            { "响亮", new string[] { "大", "Loud" } },
            { "最大", new string[] { "最大", "Max" } },
            { "账号提醒档位", new string[] { "アカウント別の通知レベル", "Account alert levels" } },
            { "语言", new string[] { "言語", "Language" } },
            { "开机启动", new string[] { "Windowsの起動時に開始", "Start with Windows" } },
            { "有更新时自动更新", new string[] { "更新時に自動アップデート", "Update automatically" } },
            { "创建桌面快捷方式", new string[] { "デスクトップにショートカットを作成", "Create desktop shortcut" } },
            { "手动选择 Codex", new string[] { "Codexを手動で選択", "Choose Codex manually" } },
            { "退出程序", new string[] { "アプリを終了", "Exit application" } }
        };
        private string T(string text)
        {
            if (text == null) return "";
            string lang = snapshot == null ? "zh" : snapshot.Language;
            if (String.IsNullOrEmpty(lang) || lang == "auto") lang = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
            string[] values;
            if (!Translations.TryGetValue(text, out values)) return text;
            if (lang == "ja") return values[0];
            if (lang == "en") return values[1];
            return text;
        }
        private string VersionCaption()
        {
            return (actions.ReadVersion == null ? typeof(QuotaSettingsForm).Assembly.GetName().Version
                : actions.ReadVersion()).ToString(4);
        }

        internal QuotaSettingsForm(SettingsActions actions, string page)
        {
            if (actions == null || actions.Read == null) throw new ArgumentNullException("actions");
            this.actions = actions;
            tips.AutoPopDelay = 12000;
            tips.InitialDelay = 350;
            tips.ReshowDelay = 150;
            tips.ShowAlways = true;
            Text = "额度显示器设置";
            TitlePrimary = "额度显示器"; TitleSecondary = "设置";
            ShowIcon = true;
            Size area = Screen.FromPoint(Cursor.Position).WorkingArea.Size;
            Size = new Size(Math.Min(S(800), area.Width), Math.Min(S(520), area.Height));
            MinimumSize = new Size(Math.Min(S(680), area.Width), Math.Min(S(430), area.Height));
            Font = UiTheme.UiFont(10.5f);
            StartPosition = FormStartPosition.CenterScreen;
            sidebar = new UiFlowPanel();
            sidebar.Name = "sidebar";
            sidebar.Dock = DockStyle.Left;
            sidebar.Width = S(152);
            sidebar.Padding = new Padding(S(10), S(8), S(10), S(8));
            sidebar.FlowDirection = FlowDirection.TopDown;
            sidebar.WrapContents = false;
            for (int i = 0; i < Pages.Length; i++)
            {
                string target = Pages[i];
                RadioButton nav = new UiNavigation();
                nav.Name = "nav-" + target;
                nav.Appearance = Appearance.Button;
                nav.TextAlign = ContentAlignment.MiddleLeft;
                nav.Size = new Size(S(132), S(36));
                nav.Margin = new Padding(0, 0, 0, S(4));
                nav.CheckedChanged += delegate { if (!updatingNavigation && nav.Checked) Navigate(target); };
                navigation[i] = nav;
                sidebar.Controls.Add(nav);
            }
            content = new UiFlowPanel();
            content.Name = "content";
            content.Dock = DockStyle.Fill;
            content.FlowDirection = FlowDirection.TopDown;
            content.WrapContents = false;
            content.AutoScroll = false;
            content.Padding = new Padding(S(18), S(15), S(18), S(14));
            content.GetCustomScroll = delegate { return new Point(0, -(int)Math.Round(scrollCurrent)); };
            content.SetCustomScroll = delegate(Point pt)
            {
                int target = pt.Y < 0 ? -pt.Y : pt.Y;
                scrollTarget = scrollCurrent = Math.Max(0, Math.Min(maxScroll, target));
                overscroll = 0;
                scrollTimer.Stop();
                ApplyCustomScroll();
            };
            content.AfterCustomLayout = delegate { CaptureBasePositionsAndApplyScroll(); };
            scrollBar = new UiScrollBar();
            scrollBar.Name = "content-scrollbar";
            scrollBar.Visible = false;
            scrollBar.Scrolled += delegate(double value)
            {
                scrollTarget = scrollCurrent = Math.Max(0, Math.Min(maxScroll, value));
                overscroll = 0;
                scrollTimer.Stop();
                ApplyCustomScroll(true);
            };
            scrollBar.WheelDelta += delegate(int delta) { HandleWheelDelta(delta); };
            scrollTimer.Tick += delegate { StepScrollAnimation(); };
            wheelFilter = new WheelFilter(this);
            Application.AddMessageFilter(wheelFilter);
            Controls.Add(content);
            Controls.Add(sidebar);
            Controls.Add(scrollBar);
            scrollBar.BringToFront();
            versionLabel = new UiLabel();
            versionLabel.Name = "version-label";
            versionLabel.AutoSize = true;
            versionLabel.Text = VersionCaption();
            Font versionFont = UiTheme.UiFont(8.5f);
            versionLabel.Font = versionFont;
            versionLabel.Disposed += delegate { versionFont.Dispose(); };
            versionLabel.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;
            versionLabel.Location = new Point(S(18), Math.Max(0, ClientSize.Height - versionLabel.PreferredHeight - S(11)));
            Controls.Add(versionLabel);
            versionLabel.BringToFront();
            if (actions.ApplyUpdate != null)
            {
                versionRange = new UpdateVersionRange { Name = "update-version-range", Font = versionFont };
                Controls.Add(versionRange);
                versionRange.BringToFront();
                updateButton = Button("更新", true, delegate { Call(actions.ApplyUpdate); RefreshReleaseStatus(); });
                updateButton.Name = "version-update";
                updateButton.AutoSize = false;
                updateButton.Size = new Size(S(116), ControlHeight);
                updateButton.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;
                updateButton.Visible = false;
                Controls.Add(updateButton);
                updateButton.BringToFront();
                ClientSizeChanged += delegate { PositionVersionFooter(); };
                versionLabel.SizeChanged += delegate { PositionVersionFooter(); };
            }
            content.SizeChanged += delegate
            {
                ResizeCards();
                if (cardLayoutQueued || !content.IsHandleCreated) return;
                cardLayoutQueued = true;
                // The scroll panel finishes its own resize after SizeChanged.
                content.BeginInvoke((MethodInvoker)delegate
                {
                    cardLayoutQueued = false;
                    if (!content.IsDisposed) content.PerformLayout();
                });
            };
            Navigate(page);
            Shown += delegate { ResizeCards(); };
            UiTheme.Hook(this);
            themeTimer.Interval = 2000;
            themeTimer.Tick += delegate { UiTheme.RefreshSystem(); RefreshReleaseStatus(); };
            themeTimer.Start();
            currentNoticeTimer.Tick += delegate { RefreshReleaseStatus(true); };
        }

        internal string CurrentPage { get { return page; } }

        internal void Navigate(string page)
        {
            string nextPage = Array.IndexOf(Pages, page) >= 0 ? page : "accounts";
            bool samePage = this.page == nextPage;
            if (samePage && nextPage == "display" && desktop != null) return;
            if (samePage && nextPage == "accounts" && snapshot != null) { RefreshAccounts(); return; }
            this.page = nextPage;
            Reload(samePage);
        }

        internal void Reload() { Reload(true); }

        private SettingsSnapshot ReadSnapshot()
        {
            SettingsSnapshot current = actions.Read() ?? new SettingsSnapshot();
            if (current.Accounts == null) current.Accounts = new List<SettingsAccount>();
            return current;
        }

        internal void RefreshAccounts()
        {
            if (page == "general") return;
            SettingsSnapshot current = ReadSnapshot();
            bool sameAccounts = current.Accounts.Count == snapshot.Accounts.Count &&
                current.AccountsLoading == snapshot.AccountsLoading && current.CodexMissing == snapshot.CodexMissing &&
                current.AccountReadError == snapshot.AccountReadError;
            for (int i = 0; sameAccounts && i < current.Accounts.Count; i++)
            {
                SettingsAccount before = snapshot.Accounts[i], after = current.Accounts[i];
                sameAccounts = before.Slot == after.Slot && before.DisplayNumber == after.DisplayNumber &&
                    before.Plan == after.Plan && before.EmailMask == after.EmailMask;
            }
            if (!sameAccounts)
            {
                Reload(true, current);
                return;
            }

            if (page != "accounts") return;
            snapshot = current;
            bool layoutChanged = false;
            foreach (SettingsAccount account in current.Accounts)
                layoutChanged |= UpdateAccountQuota(accountQuotaRows[account.Slot], account);
            if (layoutChanged) ResizeCards();
        }

        private void Reload(bool preserveScroll, SettingsSnapshot current = null)
        {
            double oldScrollY = scrollCurrent;
            snapshot = current ?? ReadSnapshot();
            FontFamily targetFamily = UiTheme.FontFamilyFor(snapshot.Language);
            if (!Equals(Font.FontFamily, targetFamily))
            {
                Font oldFont = Font;
                Font = new Font(targetFamily, 10.5f);
                foreach (RadioButton nav in navigation) nav.Font = Font;
                if (versionLabel != null)
                {
                    Font oldVerFont = versionLabel.Font;
                    Font newVerFont = new Font(targetFamily, 8.5f);
                    versionLabel.Font = newVerFont;
                    if (versionRange != null) versionRange.Font = newVerFont;
                    versionLabel.Disposed += delegate { newVerFont.Dispose(); };
                    if (oldVerFont != null && !ReferenceEquals(oldVerFont, oldFont)) oldVerFont.Dispose();
                }
                if (oldFont != null) oldFont.Dispose();
                PositionVersionFooter();
            }
            accountQuotaRows.Clear();
            Text = T("额度显示器设置");
            TitlePrimary = T("额度显示器"); TitleSecondary = T("设置"); TitleBar.Invalidate();
            if (versionLabel != null) versionLabel.Text = VersionCaption();
            content.SuspendLayout();
            while (content.Controls.Count > 0) content.Controls[0].Dispose();
            baseChildTops.Clear();
            lastAppliedOffset = int.MinValue;
            checkUpdateButton = null; updateMessage = null;
            displayToggles.Clear(); desktopSlots.Clear(); desktop = null; error = null;
            updatingNavigation = true;
            for (int i = 0; i < Pages.Length; i++)
            {
                navigation[i].Text = T(PageNames[i]);
                navigation[i].Checked = Pages[i] == page;
            }
            updatingNavigation = false;
            Label heading = TextLine(T(PageNames[Array.IndexOf(Pages, page)]), true);
            heading.Name = "page-heading"; heading.Font = new Font(Font.FontFamily, 17f, FontStyle.Bold);
            Font headingFont = heading.Font;
            heading.Disposed += delegate { headingFont.Dispose(); };
            heading.Margin = new Padding(0, 0, 0, 4); content.Controls.Add(heading);
            string subtitle = page == "accounts" ? "账号与额度" : page == "display" ? "选择通知区域与桌面显示的账号" : page == "notifications" ? "独立设置每个账号的提醒" : "语言与系统";
            Label caption = TextLine(subtitle, false); caption.Name = "page-caption"; caption.Margin = new Padding(0, 0, 0, LayoutGap); content.Controls.Add(caption);
            if (page == "accounts") AccountsPage();
            else if (page == "display") DisplayPage();
            else if (page == "notifications") NotificationsPage();
            else GeneralPage();
            RefreshReleaseStatus(true);
            UiTheme.Apply(this);
            content.ResumeLayout();
            ResizeCards();
            content.PerformLayout();
            scrollTarget = scrollCurrent = preserveScroll ? Math.Max(0, Math.Min(maxScroll, oldScrollY)) : 0;
            overscroll = 0;
            scrollTimer.Stop();
            ApplyCustomScroll();
        }

        private GroupBox Card(string slot = null)
        {
            GroupBox card = new UiCard { AccountSlot = slot }; card.AutoSize = false;
            card.Padding = new Padding(CardPaddingSize);
            card.Margin = new Padding(0, 0, 0, LayoutGap);
            FlowLayoutPanel inner = new UiFlowPanel(); inner.Name = "inner"; inner.AutoSize = false; inner.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            inner.FlowDirection = FlowDirection.TopDown;
            inner.WrapContents = false; inner.Location = new Point(CardPaddingSize, CardPaddingSize); card.Controls.Add(inner); content.Controls.Add(card);
            return card;
        }
        private FlowLayoutPanel ActionRow()
        {
            FlowLayoutPanel row = new UiFlowPanel(); row.Name = "action-row"; row.AutoSize = true; row.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            row.FlowDirection = FlowDirection.LeftToRight; row.WrapContents = true; row.Margin = new Padding(0); return row;
        }
        private FlowLayoutPanel Inner(GroupBox card) { return (FlowLayoutPanel)card.Controls["inner"]; }
        private void ResizeCards()
        {
            if (resizingCards || content == null) return;
            resizingCards = true;
            content.SuspendLayout();
            try
            {
                // The client area already excludes the visible scrollbar.
                int width = Math.Max(S(250), content.ClientSize.Width - content.Padding.Horizontal);
                foreach (Control c in content.Controls)
                {
                    GroupBox card = c as GroupBox;
                    if (card == null) { c.MaximumSize = new Size(width, 0); continue; }
                    FlowLayoutPanel inner = Inner(card);
                    int innerWidth = Math.Max(S(100), width - card.Padding.Horizontal);
                    foreach (Control child in inner.Controls)
                    {
                        Label label = child as Label;
                        if (label != null) label.MaximumSize = new Size(innerWidth - S(8), 0);
                        FlowLayoutPanel row = child as FlowLayoutPanel;
                        if (row != null)
                        {
                            row.MaximumSize = new Size(innerWidth, 0);
                            if (row.Name == "global-level-actions")
                            {
                                Control options = row.Controls["default-level"];
                                Control apply = row.Controls["apply-global-level"];
                                options.MaximumSize = new Size(innerWidth, 0);
                                int gap = innerWidth - options.GetPreferredSize(Size.Empty).Width - apply.Width;
                                apply.Margin = gap >= LayoutGap
                                    ? new Padding(gap, 0, 0, 0)
                                    : new Padding(Math.Max(0, innerWidth - apply.Width), LayoutGap, 0, 0);
                            }
                            if (row.Name == "meters")
                            {
                                int count = row.Controls.Count;
                                int meterWidth = count == 2 ? (innerWidth - LayoutGap) / 2 : innerWidth;
                                for (int i = 0; i < count; i++)
                                {
                                    Control meter = row.Controls[i];
                                    meter.Width = meterWidth;
                                    meter.Margin = new Padding(0, 0, count == 2 && i == 0 ? LayoutGap : 0, 0);
                                }
                            }
                            if (row.Name == "account-header") row.MaximumSize = new Size(innerWidth, 0);
                        }
                        if (child is QuotaMeter) child.Width = innerWidth - S(6);
                    }
                    inner.Width = innerWidth;
                    inner.PerformLayout();
                    int height = inner.GetPreferredSize(new Size(innerWidth, 0)).Height;
                    foreach (Control child in inner.Controls)
                        if (child.Visible) height = Math.Max(height, child.Top + Math.Max(child.Height, child.MinimumSize.Height) + child.Margin.Bottom);
                    inner.Size = new Size(innerWidth, height);
                    card.Size = new Size(width, height + card.Padding.Vertical);
                }
            }
            finally
            {
                content.ResumeLayout(true);
                resizingCards = false;
            }
            CaptureBasePositionsAndApplyScroll();
        }
        private void CaptureBasePositionsAndApplyScroll()
        {
            if (content == null || scrollBar == null || applyingScroll || resizingCards) return;
            baseChildTops.Clear();
            lastAppliedOffset = int.MinValue;
            int bottom = content.Padding.Top;
            foreach (Control child in content.Controls)
            {
                baseChildTops[child] = child.Top;
                if (child.Visible) bottom = Math.Max(bottom, child.Top + child.Height + child.Margin.Bottom);
            }
            int viewHeight = Math.Max(1, content.ClientSize.Height);
            totalContentHeight = Math.Max(viewHeight, bottom + content.Padding.Bottom);
            maxScroll = Math.Max(0, totalContentHeight - viewHeight);
            scrollTarget = Math.Max(0, Math.Min(maxScroll, scrollTarget));
            scrollCurrent = Math.Max(0, Math.Min(maxScroll, scrollCurrent));
            if (maxScroll == 0) overscroll = Math.Max(-S(36), Math.Min(S(36), overscroll));
            int barWidth = S(12);
            scrollBar.SetBounds(Math.Max(0, ClientSize.Width - barWidth - S(4)),
                ScaledCaptionHeight + S(4), barWidth, Math.Max(1, ClientSize.Height - ScaledCaptionHeight - S(8)));
            scrollBar.BringToFront();
            ApplyCustomScroll();
        }
        private bool MoveScrollChildren(int offset)
        {
            List<Control> targets = new List<Control>();
            List<int> tops = new List<int>();
            bool allHandles = content.IsHandleCreated;
            foreach (KeyValuePair<Control, int> entry in baseChildTops)
            {
                Control child = entry.Key;
                if (child == null || child.IsDisposed) continue;
                int targetTop = entry.Value - offset;
                if (child.Top == targetTop) continue;
                targets.Add(child);
                tops.Add(targetTop);
                if (!child.IsHandleCreated) allHandles = false;
            }
            if (targets.Count == 0) return false;
            if (allHandles)
            {
                const uint flags = 0x0001 | 0x0004 | 0x0008 | 0x0010 | 0x0100; // SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_NOREDRAW | SWP_NOCOPYBITS
                IntPtr hdwp = BeginDeferWindowPos(targets.Count);
                if (hdwp != IntPtr.Zero)
                {
                    for (int i = 0; i < targets.Count && hdwp != IntPtr.Zero; i++)
                        hdwp = DeferWindowPos(hdwp, targets[i].Handle, IntPtr.Zero, targets[i].Left, tops[i], 0, 0, flags);
                    if (hdwp != IntPtr.Zero && EndDeferWindowPos(hdwp)) return true;
                }
            }
            for (int i = 0; i < targets.Count; i++) targets[i].Top = tops[i];
            return true;
        }
        private void ApplyCustomScroll(bool immediatePaint = false)
        {
            if (content == null || applyingScroll) return;
            applyingScroll = true;
            content.SuspendLayout();
            bool moved = false;
            try
            {
                int offset = (int)Math.Round(scrollCurrent + overscroll);
                content.SuppressLayout = true;
                if (offset != lastAppliedOffset)
                {
                    moved = MoveScrollChildren(offset);
                    lastAppliedOffset = offset;
                }
                int viewHeight = Math.Max(1, content.ClientSize.Height);
                bool canScroll = totalContentHeight > viewHeight + 1;
                if (scrollBar != null)
                {
                    scrollBar.Visible = canScroll;
                    if (canScroll) scrollBar.UpdateMetrics(viewHeight, totalContentHeight, scrollCurrent, overscroll, maxScroll);
                }
            }
            finally
            {
                content.SuppressLayout = false;
                content.ResumeLayout(false);
                applyingScroll = false;
            }
            if (moved && content.IsHandleCreated && content.Visible && Visible)
            {
                if (immediatePaint)
                {
                    const uint flags = 0x0001 | 0x0004 | 0x0080 | 0x0100; // RDW_INVALIDATE | RDW_ERASE | RDW_ALLCHILDREN | RDW_UPDATENOW
                    RedrawWindow(content.Handle, IntPtr.Zero, IntPtr.Zero, flags);
                    if (scrollBar != null && scrollBar.Visible && scrollBar.IsHandleCreated) scrollBar.Update();
                }
                else
                {
                    content.Invalidate(true);
                }
            }
        }
        private void StepScrollAnimation()
        {
            double diff = scrollTarget - scrollCurrent;
            if (Math.Abs(diff) < 0.35) scrollCurrent = scrollTarget;
            else scrollCurrent += diff * 0.32;

            if (Math.Abs(overscroll) < 0.35) overscroll = 0;
            else overscroll *= 0.80;

            ApplyCustomScroll(true);
            if (Math.Abs(scrollTarget - scrollCurrent) < 0.35 && Math.Abs(overscroll) < 0.35)
            {
                scrollCurrent = scrollTarget;
                overscroll = 0;
                scrollTimer.Stop();
                ApplyCustomScroll(true);
            }
        }
        internal void HandleWheelDelta(int delta)
        {
            if (delta == 0) return;
            double step = -(delta / 120.0) * S(54);
            double desired = scrollTarget + step;
            double maxBounce = S(40);
            if (desired < 0)
            {
                double excess = desired; // negative
                scrollTarget = 0;
                double resistance = 1.0 + Math.Abs(overscroll) / S(16);
                overscroll = Math.Max(-maxBounce, overscroll + excess * 0.45 / resistance);
            }
            else if (desired > maxScroll)
            {
                double excess = desired - maxScroll; // positive
                scrollTarget = maxScroll;
                double resistance = 1.0 + Math.Abs(overscroll) / S(16);
                overscroll = Math.Min(maxBounce, overscroll + excess * 0.45 / resistance);
            }
            else
            {
                scrollTarget = desired;
            }
            if (IsHandleCreated && !scrollTimer.Enabled) scrollTimer.Start();
            ApplyCustomScroll(true);
        }
        internal void SimulateWheelForTest(int delta) { HandleWheelDelta(delta); }
        internal void StepScrollForTest(int steps = 1) { for (int i = 0; i < steps; i++) StepScrollAnimation(); }
        internal int ScrollMax { get { return maxScroll; } }
        internal double OverscrollForTest { get { return overscroll; } }
        internal UiScrollBar ScrollBarForTest { get { return scrollBar; } }
        internal string ToolTipForTest(Control control) { return control == null ? "" : tips.GetToolTip(control); }
        private bool IsInsideViewport(Control hit)
        {
            for (Control c = hit; c != null; c = c.Parent)
            {
                if (ReferenceEquals(c, content) || ReferenceEquals(c, scrollBar)) return true;
                if (c is Form && !ReferenceEquals(c, this)) return false;
            }
            return false;
        }
        private bool CanHandleWheel()
        {
            if (!Visible || !ContainsFocus && Form.ActiveForm != this) return false;
            return !HasOpenComboPopup(this);
        }
        private static bool HasOpenComboPopup(Control parent)
        {
            foreach (Control c in parent.Controls)
            {
                UiComboBox combo = c as UiComboBox;
                if (combo != null && combo.PopupOpen) return true;
                if (c.Controls.Count > 0 && HasOpenComboPopup(c)) return true;
            }
            return false;
        }
        private sealed class WheelFilter : IMessageFilter
        {
            private readonly QuotaSettingsForm owner;
            [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(Point pt);
            internal WheelFilter(QuotaSettingsForm owner) { this.owner = owner; }
            public bool PreFilterMessage(ref Message m)
            {
                if (m.Msg != 0x020A || owner.IsDisposed || !owner.Visible || !owner.CanHandleWheel()) return false;
                long lp = m.LParam.ToInt64();
                Point pt = new Point((short)(lp & 0xFFFF), (short)((lp >> 16) & 0xFFFF));
                Control hit = Control.FromChildHandle(WindowFromPoint(pt));
                if (!owner.IsInsideViewport(hit)) return false;
                int delta = (short)((m.WParam.ToInt64() >> 16) & 0xFFFF);
                owner.HandleWheelDelta(delta);
                return true;
            }
        }
        private Label TextLine(string text, bool bold)
        {
            Label l = new UiLabel(); l.Text = T(text); l.AutoSize = true; l.MaximumSize = new Size(S(760), 0);
            l.Margin = new Padding(0, 0, 0, LayoutGap); if (bold) l.Font = new Font(Font, FontStyle.Bold); return l;
        }
        private Button Button(string text, bool accent, EventHandler click)
        {
            UiButton b = new UiButton(); b.Accent = accent; b.Text = T(text); b.AutoSize = true;
            b.MinimumSize = new Size(S(100), ControlHeight); b.Height = ControlHeight; b.Margin = new Padding(0, 0, LayoutGap, 0); b.Click += click; return b;
        }
        private FlowLayoutPanel Row()
        {
            FlowLayoutPanel row = new UiFlowPanel(); row.AutoSize = true; row.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            row.WrapContents = true; row.Margin = new Padding(0); return row;
        }
        private void Call(Action action) { if (action != null) action(); }
        private void Reading(FlowLayoutPanel target)
        {
            string status = snapshot.AccountsLoading ? "正在读取账号…" : snapshot.AccountReadError ?? "未绑定任何账号";
            target.Controls.Add(TextLine(status, false));
            if (snapshot.CodexMissing) AddMissingCodex(target);
        }

        private void AddMissingCodex(FlowLayoutPanel target)
        {
            Label missing = TextLine("未识别到 Codex", false);
            missing.Name = "account-status-warning";
            target.Controls.Add(missing);
        }

        private string AccountNumber(SettingsAccount account)
        {
            return String.Format(T("账号 {0}"), account.DisplayNumber > 0 ? account.DisplayNumber : UiTheme.AccountNumber(account.Slot));
        }
        private string AccountSummary(SettingsAccount account)
        {
            if (String.IsNullOrEmpty(account.Plan) && String.IsNullOrEmpty(account.EmailMask) && !String.IsNullOrEmpty(account.Label)) return account.Label;
            string plan = String.IsNullOrEmpty(account.Plan) ? "" : account.Plan;
            string email = String.IsNullOrEmpty(account.EmailMask) ? "" : account.EmailMask;
            string tail = String.IsNullOrEmpty(plan) ? email : plan + (String.IsNullOrEmpty(email) ? "" : "  |  " + email);
            return AccountNumber(account) + (String.IsNullOrEmpty(tail) ? "" : "  |  " + tail);
        }
        private Control AccountHeader(SettingsAccount account)
        {
            FlowLayoutPanel row = new UiFlowPanel(); row.Name = "account-header"; row.AutoSize = true; row.AutoSizeMode = AutoSizeMode.GrowAndShrink; row.WrapContents = false; row.Margin = new Padding(0, 0, 0, LayoutGap);
            Label number = new UiLabel(); number.AutoSize = true; number.Text = AccountNumber(account); number.Name = "account-number"; number.Tag = account.Slot; number.Font = new Font(Font, FontStyle.Bold); number.Margin = new Padding(0, 0, S(8), 0); row.Controls.Add(number);
            Label divider = new UiLabel(); divider.AutoSize = true; divider.Text = "|"; divider.ForeColor = UiTheme.Border; divider.Margin = new Padding(0, 0, S(8), 0); row.Controls.Add(divider);
            Label plan = new UiLabel(); plan.AutoSize = true; plan.Text = String.IsNullOrEmpty(account.Plan) ? "" : account.Plan; plan.ForeColor = UiTheme.TextColor; plan.Font = new Font(Font, FontStyle.Bold); plan.Margin = new Padding(0, 0, S(8), 0); if (!String.IsNullOrEmpty(plan.Text)) row.Controls.Add(plan);
            Label email = new UiLabel(); email.AutoSize = true; email.Text = account.EmailMask ?? ""; email.ForeColor = UiTheme.Muted; email.Margin = new Padding(0); if (!String.IsNullOrEmpty(email.Text)) row.Controls.Add(email);
            return row;
        }

        private void AccountsPage()
        {
            if (snapshot.Accounts.Count == 0)
            {
                GroupBox empty = Card(); Reading(Inner(empty));
            }
            else if (snapshot.CodexMissing) AddMissingCodex(Inner(Card()));
            foreach (SettingsAccount account in snapshot.Accounts)
            {
                GroupBox card = Card(account.Slot); FlowLayoutPanel inner = Inner(card);
                inner.Controls.Add(AccountHeader(account));
                Label errorLine = TextLine("", false);
                errorLine.Name = "quota-error";
                inner.Controls.Add(errorLine);
                FlowLayoutPanel meters = Row(); meters.Name = "meters"; inner.Controls.Add(meters);
                Label creditLine = TextLine("", false);
                creditLine.Name = "reset-credits-line";
                creditLine.Visible = false;
                creditLine.Margin = Padding.Empty;
                Font creditFont = new Font(Font.FontFamily, 9.2f);
                creditLine.Font = creditFont;
                creditLine.Disposed += delegate { creditFont.Dispose(); };
                inner.Controls.Add(creditLine);
                accountQuotaRows[account.Slot] = meters;
                UpdateAccountQuota(meters, account);
            }
            GroupBox top = Card(); FlowLayoutPanel row = ActionRow();
            row.Controls.Add(Button("绑定或更改账号", true, delegate { Call(actions.ConnectAccounts); }));
            row.Controls.Add(Button("刷新额度", false, delegate { Call(actions.RefreshAll); }));
            row.Controls.Add(Button("手动选择 Codex", false, delegate { Call(actions.LocateCodex); }));
            EqualizeActions(row);
            Inner(top).Controls.Add(row);
        }
        private bool UpdateAccountQuota(FlowLayoutPanel row, SettingsAccount account)
        {
            Control errorLine = row.Parent.Controls["quota-error"];
            string errorText = account.Failed ? T("查询失败，显示上次结果") : "";
            bool changed = errorLine.Text != errorText;
            errorLine.Text = errorText;
            errorLine.Visible = account.Failed;

            Label creditLine = row.Parent.Controls["reset-credits-line"] as Label;
            if (creditLine != null)
            {
                string creditText = "";
                bool creditWarning = false;
                if (account.ResetCredits != null && account.ResetCredits.availableCount > 0)
                {
                    double soonest = ResetCreditTracker.SoonestExpiry(account.ResetCredits, DateTimeOffset.UtcNow);
                    string clock = NotifyRules.ResetCreditExpiryClock(soonest);
                    creditText = String.IsNullOrEmpty(clock)
                        ? L.F("重置券：{0} 张", account.ResetCredits.availableCount)
                        : L.F("重置券：{0} 张 · 最近 {1} 过期", account.ResetCredits.availableCount, clock);
                    creditWarning = soonest > 0 && (soonest - DateTimeOffset.UtcNow.ToUnixTimeSeconds()) <= ResetCreditTracker.ExpiringSoonSeconds;
                }
                bool showCredit = creditText.Length > 0;
                if (creditLine.Text != creditText || creditLine.Visible != showCredit)
                {
                    creditLine.Text = creditText;
                    creditLine.Visible = showCredit;
                    creditLine.Margin = showCredit ? new Padding(0, S(8), 0, 0) : Padding.Empty;
                    changed = true;
                }
                creditLine.Tag = creditWarning;
                creditLine.ForeColor = creditWarning
                    ? UiTheme.WarningText
                    : UiTheme.Accent;
            }

            WindowInfo shortWindow = null, longWindow = null;
            foreach (WindowInfo window in new[] { account.Primary, account.Secondary })
            {
                if (!QuotaMeter.Valid(window)) continue;
                if (QuotaWindow.IsLong(window.windowMins)) longWindow = window; else shortWindow = window;
            }
            bool longExhausted = longWindow != null && QuotaWindow.Remaining(longWindow.used) <= 0;
            int count = shortWindow != null && longWindow != null ? 2 : 1;
            changed |= row.Controls.Count != count;
            while (row.Controls.Count > count) row.Controls[row.Controls.Count - 1].Dispose();
            for (int i = 0; i < count; i++)
            {
                WindowInfo window = i == 0 && shortWindow != null ? shortWindow : longWindow;
                bool superseded = window != null && window == shortWindow && longExhausted;
                string title = window == null ? L.T("正在读取额度…") : window == shortWindow ? L.T("5 小时额度") :
                    L.F("{0} 天额度", QuotaWindow.Days(window.windowMins));
                if (i < row.Controls.Count) ((QuotaMeter)row.Controls[i]).UpdateQuota(window, title, superseded);
                else
                {
                    var meter = new QuotaMeter(window, title, superseded);
                    meter.BackColor = row.BackColor;
                    row.Controls.Add(meter);
                }
            }
            return changed;
        }

        private void DisplayPage()
        {
            GroupBox card = Card(); FlowLayoutPanel inner = Inner(card);
            inner.Controls.Add(TextLine("外观", true));
            string[] themes = { "auto", "light", "dark" };
            inner.Controls.Add(ChoiceRow("theme", Appearances,
                Array.IndexOf(themes, UiTheme.Normalize(snapshot.Theme)),
                delegate(int choice) { if (actions.SetTheme != null) actions.SetTheme(themes[choice]); }));
            card = Card(); inner = Inner(card);
            inner.Controls.Add(TextLine("选择要显示的账号", true));
            if (snapshot.Accounts.Count == 0) { Reading(inner); inner.Controls.Add(Button("绑定或更改账号", true, delegate { Call(actions.ConnectAccounts); })); }
            for (int i = 0; i < snapshot.Accounts.Count; i++)
            {
                SettingsAccount account = snapshot.Accounts[i];
                FlowLayoutPanel row = Row(); CheckBox toggle = new UiCheckBox(); toggle.Name = "display-" + account.Slot;
                toggle.Tag = account.Slot; toggle.Text = AccountSummary(account); toggle.AutoSize = true;
                toggle.Checked = snapshot.Accounts.Count == 1 || account.Visible; toggle.Enabled = snapshot.Accounts.Count > 1; toggle.Margin = new Padding(0);
                row.Margin = new Padding(0, 0, 0, i == snapshot.Accounts.Count - 1 ? 0 : LayoutGap);
                row.Controls.Add(toggle); inner.Controls.Add(row); displayToggles.Add(toggle);
            }
            if (snapshot.Accounts.Count == 1)
            {
                Label hint = HelpText("请至少选择一个显示账号。", "min-visible-accounts");
                hint.Margin = new Padding(0, 8, 0, 0);
                inner.Controls.Add(hint);
            }
            card = Card(); inner = Inner(card);
            inner.Controls.Add(TextLine("桌面快捷方式图标", true));
            desktop = new UiComboBox(); desktop.Name = "desktop-source";  desktop.Width = LongSelectWidth;
            desktop.Items.Add(T("自动")); desktopSlots.Add("auto");
            foreach (SettingsAccount account in snapshot.Accounts) { desktop.Items.Add(AccountSummary(account)); desktopSlots.Add(account.Slot); }
            int selected = desktopSlots.IndexOf(snapshot.DesktopSlot ?? "auto"); desktop.SelectedIndex = selected < 0 ? 0 : selected;
            inner.Controls.Add(desktop);
            error = TextLine("", false); error.Name = "display-error"; error.Visible = false; error.Margin = new Padding(0); inner.Controls.Add(error);
            Button save = Button("保存显示设置", true, SaveDisplay); save.Name = "save-display"; inner.Controls.Add(save);
        }
        private void SetDisplayError(string text)
        {
            string next = text ?? "";
            bool show = !String.IsNullOrEmpty(next);
            if (error.Text == next && error.Visible == show) return;
            error.Text = next;
            error.Visible = show;
            error.Margin = new Padding(0, 0, 0, show ? LayoutGap : 0);
            ResizeCards();
        }
        private void SaveDisplay(object sender, EventArgs e)
        {
            List<string> selected = new List<string>();
            foreach (CheckBox toggle in displayToggles) if (toggle.Checked) selected.Add((string)toggle.Tag);
            if (selected.Count == 0) { SetDisplayError(T("请至少选择一个显示账号。")); return; }
            if (actions.SaveDisplay == null || !actions.SaveDisplay(selected.ToArray(), desktopSlots[desktop.SelectedIndex]))
            { SetDisplayError(T("保存失败，请重试。")); return; }
            SetDisplayError(null);
        }
        private FlowLayoutPanel ChoiceRow(string name, string[] labels, int selected, Action<int> choose, string[] descriptions = null, string accountSlot = null, bool firstIsDanger = false)
        {
            FlowLayoutPanel row = ActionRow();
            row.Name = name;
            int width = ChoiceWidth();
            for (int i = 0; i < labels.Length; i++)
            {
                int index = i;
                var option = new UiChoice { AccountSlot = accountSlot, Text = T(labels[i]), Size = new Size(width, ControlHeight),
                    Checked = i == selected, Danger = firstIsDanger && i == 0 };
                option.Margin = new Padding(0, 0, i == labels.Length - 1 ? 0 : 6, 3);
                if (descriptions != null)
                {
                    option.AccessibleDescription = T(descriptions[i]);
                    tips.SetToolTip(option, option.AccessibleDescription);
                }
                option.CheckedChanged += delegate { if (option.Checked && choose != null) choose(index); };
                row.Controls.Add(option);
            }
            return row;
        }

        private int ChoiceWidth()
        {
            int width = S(88);
            int pad = S(16);
            using (Font zhFont = UiTheme.UiFont(Font.SizeInPoints, Font.Style, "zh"))
            using (Font sansFont = UiTheme.UiFont(Font.SizeInPoints, Font.Style, "en"))
            {
                foreach (string label in Levels.Concat(Appearances))
                {
                    width = Math.Max(width, TextRenderer.MeasureText(label, zhFont).Width + pad);
                    width = Math.Max(width, TextRenderer.MeasureText(label, sansFont).Width + pad);
                    foreach (string translation in Translations[label])
                        width = Math.Max(width, TextRenderer.MeasureText(translation, sansFont).Width + pad);
                }
            }
            return width;
        }

        private FlowLayoutPanel LevelChoices(string name, int level, Action<int> set, string accountSlot = null)
        {
            return ChoiceRow(name, Levels, Math.Max(0, Math.Min(3, level + 1)),
                delegate(int selected) { if (set != null) set(selected - 1); }, LevelTips, accountSlot, firstIsDanger: true);
        }

        private Label HelpText(string text, string name)
        {
            Label help = TextLine(text, false);
            help.Name = name;
            Font small = new Font(Font.FontFamily, 9f);
            help.Font = small;
            help.Disposed += delegate { small.Dispose(); };
            return help;
        }

        private void NotificationsPage()
        {
            GroupBox card = Card(); FlowLayoutPanel inner = Inner(card);
            inner.Controls.Add(TextLine("默认提醒档位", true));
            inner.Controls.Add(HelpText("应用后统一已绑定账号的档位，新绑定的账号也默认使用此档位。", "global-level-help"));
            FlowLayoutPanel global = ActionRow();
            global.Name = "global-level-actions";
            Label applyError = HelpText("", "global-level-error");
            applyError.Visible = false;
            applyError.Margin = new Padding(0, LayoutGap, 0, 0);
            UiButton apply = (UiButton)Button("应用", true, delegate
            {
                if (!pendingGlobalLevel.HasValue) return;
                if (actions.ApplyGlobalLevel == null || !actions.ApplyGlobalLevel(pendingGlobalLevel.Value))
                {
                    applyError.Text = T("保存失败，请重试。");
                    applyError.Visible = true;
                    ResizeCards();
                    return;
                }
                pendingGlobalLevel = null;
                Reload(true);
            });
            apply.Name = "apply-global-level";
            apply.AutoSize = false;
            apply.Size = new Size(S(100), ControlHeight);
            Action<int> select = delegate(int level)
            {
                pendingGlobalLevel = level;
                apply.Enabled = level != snapshot.DefaultNotifyLevel || snapshot.Accounts.Any(a => a.NotifyLevel != level);
                apply.Invalidate();
                if (applyError.Visible) { applyError.Visible = false; ResizeCards(); }
            };
            var choicesRow = LevelChoices("default-level", pendingGlobalLevel ?? snapshot.DefaultNotifyLevel, select);
            for (int i = 0; i < choicesRow.Controls.Count; i++)
            {
                int level = i - 1;
                // Clicking the already selected default can still unify per-account overrides.
                choicesRow.Controls[i].Click += delegate { select(level); };
            }
            apply.Enabled = pendingGlobalLevel.HasValue && (pendingGlobalLevel.Value != snapshot.DefaultNotifyLevel ||
                snapshot.Accounts.Any(account => account.NotifyLevel != pendingGlobalLevel.Value));
            global.Controls.Add(choicesRow);
            global.Controls.Add(apply);
            inner.Controls.Add(global);
            inner.Controls.Add(applyError);

            var sound = new UiCheckBox { Name = "notify-sound", Text = T("播放提醒声音"), AutoSize = true,
                Checked = snapshot.NotifySound, Enabled = actions.SetNotifySound != null, Margin = new Padding(0, LayoutGap, 0, 0) };
            Label soundError = HelpText("保存失败，请重试。", "notify-sound-error");
            soundError.Visible = false;
            FlowLayoutPanel volumeRow = NotifyVolumeRow(soundError);
            bool restoringSound = false;
            sound.CheckedChanged += delegate
            {
                if (restoringSound) return;
                bool saved = actions.SetNotifySound != null && actions.SetNotifySound(sound.Checked);
                if (saved)
                {
                    snapshot.NotifySound = sound.Checked;
                    volumeRow.Enabled = sound.Checked;
                }
                else
                {
                    restoringSound = true;
                    sound.Checked = snapshot.NotifySound;
                    restoringSound = false;
                }
                bool showError = !saved;
                if (soundError.Visible != showError)
                {
                    soundError.Visible = showError;
                    ResizeCards();
                }
            };
            inner.Controls.Add(sound);
            inner.Controls.Add(volumeRow);
            inner.Controls.Add(soundError);

            var accountPaceSwitches = new Dictionary<string, UiCheckBox>(StringComparer.Ordinal);
            bool updatingAccountPace = false;
            var defaultDailyPace = new UiCheckBox
            {
                Name = "default-daily-pace",
                Text = T("推荐用量提醒"),
                AutoSize = true,
                Checked = snapshot.DefaultDailyPace,
                Enabled = actions.ApplyGlobalDailyPace != null,
                Margin = new Padding(0, LayoutGap, 0, 0)
            };
            Label dailyPaceHelp = HelpText(DailyPaceHelpKey, "global-daily-pace-help");
            string dailyPaceTip = T(DailyPaceTipKey);
            defaultDailyPace.AccessibleDescription = dailyPaceTip;
            dailyPaceHelp.AccessibleDescription = dailyPaceTip;
            tips.SetToolTip(defaultDailyPace, dailyPaceTip);
            tips.SetToolTip(dailyPaceHelp, dailyPaceTip);
            Label dailyPaceError = HelpText("保存失败，请重试。", "daily-pace-error");
            dailyPaceError.Visible = false;
            bool restoringDefaultDailyPace = false;
            defaultDailyPace.CheckedChanged += delegate
            {
                if (restoringDefaultDailyPace) return;
                bool requested = defaultDailyPace.Checked;
                bool saved = actions.ApplyGlobalDailyPace != null && actions.ApplyGlobalDailyPace(requested);
                if (saved)
                {
                    snapshot.DefaultDailyPace = requested;
                    updatingAccountPace = true;
                    foreach (SettingsAccount account in snapshot.Accounts)
                    {
                        account.DailyPace = requested;
                        UiCheckBox accountSwitch;
                        if (accountPaceSwitches.TryGetValue(account.Slot, out accountSwitch))
                            accountSwitch.Checked = requested;
                    }
                    updatingAccountPace = false;
                }
                else
                {
                    restoringDefaultDailyPace = true;
                    defaultDailyPace.Checked = snapshot.DefaultDailyPace;
                    restoringDefaultDailyPace = false;
                }
                if (dailyPaceError.Visible == saved)
                {
                    dailyPaceError.Visible = !saved;
                    ResizeCards();
                }
            };
            inner.Controls.Add(defaultDailyPace);
            inner.Controls.Add(dailyPaceHelp);
            inner.Controls.Add(dailyPaceError);

            var accountResetCreditSwitches = new Dictionary<string, UiCheckBox>(StringComparer.Ordinal);
            bool updatingAccountResetCredit = false;
            var defaultResetCredit = new UiCheckBox
            {
                Name = "default-reset-credit",
                Text = T("重置券提醒"),
                AutoSize = true,
                Checked = snapshot.DefaultResetCredit,
                Enabled = actions.ApplyGlobalResetCredit != null,
                Margin = new Padding(0, LayoutGap, 0, 0)
            };
            Label resetCreditHelp = HelpText(ResetCreditHelpKey, "global-reset-credit-help");
            string resetCreditTip = T(ResetCreditTipKey);
            defaultResetCredit.AccessibleDescription = resetCreditTip;
            resetCreditHelp.AccessibleDescription = resetCreditTip;
            tips.SetToolTip(defaultResetCredit, resetCreditTip);
            tips.SetToolTip(resetCreditHelp, resetCreditTip);
            Label resetCreditError = HelpText("保存失败，请重试。", "reset-credit-error");
            resetCreditError.Visible = false;
            bool restoringDefaultResetCredit = false;
            defaultResetCredit.CheckedChanged += delegate
            {
                if (restoringDefaultResetCredit) return;
                bool requested = defaultResetCredit.Checked;
                bool saved = actions.ApplyGlobalResetCredit != null && actions.ApplyGlobalResetCredit(requested);
                if (saved)
                {
                    snapshot.DefaultResetCredit = requested;
                    updatingAccountResetCredit = true;
                    foreach (SettingsAccount account in snapshot.Accounts)
                    {
                        account.ResetCredit = requested;
                        UiCheckBox accountSwitch;
                        if (accountResetCreditSwitches.TryGetValue(account.Slot, out accountSwitch))
                            accountSwitch.Checked = requested;
                    }
                    updatingAccountResetCredit = false;
                }
                else
                {
                    restoringDefaultResetCredit = true;
                    defaultResetCredit.Checked = snapshot.DefaultResetCredit;
                    restoringDefaultResetCredit = false;
                }
                if (resetCreditError.Visible == saved)
                {
                    resetCreditError.Visible = !saved;
                    ResizeCards();
                }
            };
            inner.Controls.Add(defaultResetCredit);
            inner.Controls.Add(resetCreditHelp);
            inner.Controls.Add(resetCreditError);
            if (snapshot.Accounts.Count == 0)
            {
                inner = Inner(Card());
                inner.Controls.Add(TextLine("账号提醒档位", true));
                Reading(inner);
                inner.Controls.Add(Button("绑定或更改账号", true, delegate { Call(actions.ConnectAccounts); }));
                return;
            }

            Label group = TextLine("账号提醒档位", true);
            group.Margin = new Padding(0, 2, 0, 10);
            content.Controls.Add(group);
            foreach (SettingsAccount account in snapshot.Accounts)
            {
                SettingsAccount current = account;
                inner = Inner(Card(current.Slot));
                inner.Controls.Add(AccountHeader(current));
                inner.Controls.Add(LevelChoices("level-" + current.Slot, current.NotifyLevel,
                    delegate(int value) { if (actions.SetAccountLevel != null) actions.SetAccountLevel(current.Slot, value); current.NotifyLevel = value;
                        if (pendingGlobalLevel.HasValue) select(pendingGlobalLevel.Value); }, current.Slot));
                var accountPace = new UiCheckBox
                {
                    Name = "daily-pace-" + current.Slot,
                    Text = T("推荐用量提醒"),
                    AutoSize = true,
                    Checked = current.DailyPace,
                    Enabled = actions.SetAccountDailyPace != null,
                    Margin = new Padding(0, LayoutGap, 0, 0)
                };
                accountPace.AccessibleDescription = dailyPaceTip;
                tips.SetToolTip(accountPace, dailyPaceTip);
                Label accountPaceError = HelpText("保存失败，请重试。", "daily-pace-error-" + current.Slot);
                accountPaceError.Visible = false;
                accountPace.CheckedChanged += delegate
                {
                    if (updatingAccountPace) return;
                    bool requested = accountPace.Checked;
                    bool saved = actions.SetAccountDailyPace != null && actions.SetAccountDailyPace(current.Slot, requested);
                    if (saved) current.DailyPace = requested;
                    else
                    {
                        updatingAccountPace = true;
                        accountPace.Checked = current.DailyPace;
                        updatingAccountPace = false;
                    }
                    if (accountPaceError.Visible == saved)
                    {
                        accountPaceError.Visible = !saved;
                        ResizeCards();
                    }
                };
                accountPaceSwitches[current.Slot] = accountPace;
                inner.Controls.Add(accountPace);
                inner.Controls.Add(accountPaceError);

                var accountResetCredit = new UiCheckBox
                {
                    Name = "reset-credit-" + current.Slot,
                    Text = T("重置券提醒"),
                    AutoSize = true,
                    Checked = current.ResetCredit,
                    Enabled = actions.SetAccountResetCredit != null,
                    Margin = new Padding(0, LayoutGap, 0, 0)
                };
                accountResetCredit.AccessibleDescription = resetCreditTip;
                tips.SetToolTip(accountResetCredit, resetCreditTip);
                Label accountResetCreditError = HelpText("保存失败，请重试。", "reset-credit-error-" + current.Slot);
                accountResetCreditError.Visible = false;
                accountResetCredit.CheckedChanged += delegate
                {
                    if (updatingAccountResetCredit) return;
                    bool requested = accountResetCredit.Checked;
                    bool saved = actions.SetAccountResetCredit != null && actions.SetAccountResetCredit(current.Slot, requested);
                    if (saved) current.ResetCredit = requested;
                    else
                    {
                        updatingAccountResetCredit = true;
                        accountResetCredit.Checked = current.ResetCredit;
                        updatingAccountResetCredit = false;
                    }
                    if (accountResetCreditError.Visible == saved)
                    {
                        accountResetCreditError.Visible = !saved;
                        ResizeCards();
                    }
                };
                accountResetCreditSwitches[current.Slot] = accountResetCredit;
                inner.Controls.Add(accountResetCredit);
                inner.Controls.Add(accountResetCreditError);
            }
        }
        private FlowLayoutPanel NotifyVolumeRow(Label soundError)
        {
            FlowLayoutPanel row = ActionRow();
            row.Name = "notify-volume-row";
            row.Margin = new Padding(0, S(4), 0, 0);
            row.Enabled = snapshot.NotifySound;
            Label label = TextLine("提醒音量", false);
            label.Name = "notify-volume-label";
            label.AutoSize = false;
            label.Size = new Size(TextRenderer.MeasureText(label.Text, Font).Width + S(2), ControlHeight);
            label.TextAlign = ContentAlignment.MiddleLeft;
            label.Margin = new Padding(0, 0, S(6), 0);
            var volume = new UiPillSlider(new[] { 0, 25, 50, 75, 100 },
                new[] { T("静音"), T("轻声"), T("适中"), T("响亮"), T("最大") })
            {
                Name = "notify-volume", AccessibleName = label.Text,
                Size = new Size(S(204), ControlHeight + S(18)), Margin = Padding.Empty,
                Value = snapshot.NotifyVolume, Enabled = actions.SetNotifyVolume != null,
                Font = new Font(Font.FontFamily, 8.5f)
            };
            Font scaleFont = volume.Font;
            volume.Disposed += delegate { scaleFont.Dispose(); };
            Label percent = TextLine("", false);
            percent.AutoSize = false;
            percent.Name = "notify-volume-value";
            percent.Size = new Size(S(44), ControlHeight);
            percent.TextAlign = ContentAlignment.MiddleRight;
            percent.Margin = new Padding(0, 0, S(8), 0);
            percent.Text = volume.Value + "%";
            var test = new UiButton
            {
                Name = "test-notify-volume", Text = T("试听"), Size = new Size(S(60), ControlHeight),
                Margin = Padding.Empty, Enabled = actions.TestNotifyVolume != null
            };
            test.Click += delegate { if (actions.TestNotifyVolume != null) actions.TestNotifyVolume(volume.Value); };

            bool restoring = false;
            Action save = delegate
            {
                if (restoring || actions.SetNotifyVolume == null || snapshot.NotifyVolume == volume.Value) return;
                bool saved = actions.SetNotifyVolume(volume.Value);
                if (saved) snapshot.NotifyVolume = volume.Value;
                else
                {
                    restoring = true;
                    volume.Value = snapshot.NotifyVolume;
                    restoring = false;
                }
                if (soundError.Visible == saved)
                {
                    soundError.Visible = !saved;
                    ResizeCards();
                }
            };
            volume.DragCompleted += delegate { save(); };
            volume.ValueChanged += delegate
            {
                percent.Text = volume.Value + "%";
                if (!volume.IsDragging) save();
            };
            row.Controls.AddRange(new Control[] { label, volume, percent, test });
            return row;
        }

        private void GeneralPage()
        {
            GroupBox card = Card(); FlowLayoutPanel inner = Inner(card);
            inner.Controls.Add(TextLine("语言", true));
            UiComboBox language = new UiComboBox(); language.Name = "language";  language.Width = ShortSelectWidth;
            string[] values = { "auto", "zh", "ja", "en" }; language.Items.AddRange(new string[] { T("自动"), "中文", "日本語", "English" });
            int index = Array.IndexOf(values, snapshot.Language ?? "auto"); language.SelectedIndex = index < 0 ? 0 : index;
            language.SelectedIndexChanged += delegate { if (actions.SetLanguage != null) actions.SetLanguage(values[language.SelectedIndex]); };
            inner.Controls.Add(language);
            FlowLayoutPanel row = Row();
            CheckBox startup = new UiCheckBox(); startup.Name = "auto-start"; startup.Text = T("开机启动"); startup.AutoSize = true; startup.Checked = snapshot.AutoStart;
            startup.Margin = new Padding(0, S(3), 0, S(3));
            bool changingStartup = false;
            startup.CheckedChanged += delegate {
                if (changingStartup) return;
                bool requested = startup.Checked;
                if (actions.SetAutoStart == null || !actions.SetAutoStart(requested))
                { changingStartup = true; startup.Checked = !requested; changingStartup = false; }
            };
            row.Controls.Add(startup); inner.Controls.Add(row);
            card = Card(); inner = Inner(card); inner.Controls.Add(TextLine("程序与快捷方式", true));
            FlowLayoutPanel buttons = ActionRow();
            UiButton shortcut = (UiButton)Button("创建桌面快捷方式", false, delegate { Call(actions.CreateShortcut); });
            shortcut.IconKind = "shortcut"; shortcut.CenterIconGroup = true;
            UiButton exit = (UiButton)Button("退出程序", false, delegate { Call(actions.Exit); });
            exit.Danger = true; exit.IconKind = "exit"; exit.CenterIconGroup = true;
            buttons.Controls.Add(shortcut); buttons.Controls.Add(exit);
            EqualizeActions(buttons);
            inner.Controls.Add(buttons);
            if (actions.CheckUpdate != null)
            {
                card = Card();
                inner = Inner(card);
                Label heading = TextLine("软件更新", true);
                heading.Margin = new Padding(0, 0, 0, S(8));
                inner.Controls.Add(heading);
                FlowLayoutPanel autoRow = Row();
                autoRow.Margin = new Padding(0, 0, 0, LayoutGap);
                var autoUpdate = new UiCheckBox
                {
                    Name = "auto-update",
                    Text = T("有更新时自动更新"),
                    AutoSize = true,
                    Checked = snapshot.AutoUpdate,
                    Enabled = actions.SetAutoUpdate != null,
                    Margin = new Padding(0)
                };
                bool changingAutoUpdate = false;
                autoUpdate.CheckedChanged += delegate
                {
                    if (changingAutoUpdate) return;
                    bool requested = autoUpdate.Checked;
                    if (actions.SetAutoUpdate != null && actions.SetAutoUpdate(requested))
                        snapshot.AutoUpdate = requested;
                    else
                    {
                        changingAutoUpdate = true;
                        autoUpdate.Checked = !requested;
                        changingAutoUpdate = false;
                    }
                };
                autoRow.Controls.Add(autoUpdate);
                inner.Controls.Add(autoRow);
                checkUpdateButton = Button("检查更新", false, delegate { Call(actions.CheckUpdate); RefreshReleaseStatus(); });
                checkUpdateButton.Name = "check-update";
                inner.Controls.Add(checkUpdateButton);
                updateMessage = TextLine("", false);
                updateMessage.Name = "update-status";
                Font statusFont = new Font(Font.FontFamily, 9f);
                updateMessage.Font = statusFont;
                updateMessage.Disposed += delegate { statusFont.Dispose(); };
                updateMessage.Margin = new Padding(CardPaddingSize, S(2), 0, LayoutGap);
                updateMessage.AccessibleRole = AccessibleRole.StaticText;
                content.Controls.Add(updateMessage);
            }
        }

        private void PositionVersionFooter()
        {
            if (updateButton == null) return;
            bool available = updateStatus != null && updateStatus.Version != null;
            int bottom = Math.Max(0, ClientSize.Height - S(available ? 21 : 11));
            updateButton.Location = new Point(S(18), bottom - updateButton.Height);
            if (!available)
            {
                versionLabel.Location = new Point(S(18), bottom - versionLabel.PreferredHeight);
                return;
            }
            int height = versionLabel.PreferredHeight;
            versionRange.SetBounds(updateButton.Left, updateButton.Top - S(8) - height, updateButton.Width, height);
        }

        internal void RefreshReleaseStatus(bool force = false)
        {
            if (actions.ReadUpdate == null) return;
            UpdateStatus next = actions.ReadUpdate();
            if (!force && updateStatus == next) return;
            updateStatus = next;
            currentNoticeTimer.Stop();
            bool busy = next != null && next.Busy;
            bool hasVersion = next != null && next.Version != null;
            if (updateButton != null)
            {
                versionLabel.Text = VersionCaption();
                versionLabel.Visible = !hasVersion;
                versionRange.Visible = hasVersion;
                if (hasVersion) versionRange.SetVersions(versionLabel.Text, next.Version.ToString(4));
                updateButton.Text = T("更新");
                updateButton.Visible = hasVersion;
                updateButton.Enabled = hasVersion && !busy;
                tips.SetToolTip(updateButton, hasVersion ? String.Format(T("可更新到 {0}"), next.Version) : "");
                PositionVersionFooter();
            }
            if (checkUpdateButton != null) checkUpdateButton.Enabled = !busy;
            if (updateMessage == null) return;
            string text = "";
            switch (next == null ? UpdatePhase.Idle : next.Phase)
            {
                case UpdatePhase.Checking: text = T("正在检查更新…"); break;
                case UpdatePhase.Current:
                    int remaining = next.CurrentNoticeRemaining(System.Diagnostics.Stopwatch.GetTimestamp());
                    if (remaining > 0)
                    {
                        text = T("已是最新版本");
                        currentNoticeTimer.Interval = remaining;
                        currentNoticeTimer.Start();
                    }
                    break;
                case UpdatePhase.Available: text = String.Format(T("可更新到 {0}"), next.Version); break;
                case UpdatePhase.Downloading: text = String.Format(T("正在下载 {0}…"), next.Version); break;
                case UpdatePhase.Installing: text = T("正在重启并应用更新…"); break;
                case UpdatePhase.CheckFailed: text = T("检查失败，请重试。"); break;
                case UpdatePhase.InstallFailed: text = T("更新失败，请重试。"); break;
            }
            updateMessage.Text = text;
            updateMessage.Tag = next != null && next.Phase == UpdatePhase.Available;
            updateMessage.Visible = text.Length > 0;
            UiTheme.Apply(updateMessage);
            ResizeCards();
        }
        private void EqualizeActions(FlowLayoutPanel row)
        {
            int width = S(100);
            int textPad = S(32), iconPad = S(58);
            foreach (UiButton button in row.Controls)
                width = Math.Max(width, TextRenderer.MeasureText(button.Text, Font).Width + (String.IsNullOrEmpty(button.IconKind) ? textPad : iconPad));
            int wrapGap = S(6);
            foreach (UiButton button in row.Controls)
            {
                button.AutoSize = false;
                button.Size = new Size(width, ControlHeight);
                button.Margin = new Padding(0, 0, LayoutGap, wrapGap);
            }
            row.Controls[row.Controls.Count - 1].Margin = Padding.Empty;
        }

        private sealed class UpdateVersionRange : Control
        {
            private string current = "", next = "";

            internal UpdateVersionRange()
            {
                AccessibleRole = AccessibleRole.StaticText;
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                    ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            }

            internal void SetVersions(string oldVersion, string newVersion)
            {
                current = oldVersion;
                next = newVersion;
                AccessibleName = current + " → " + next;
                Invalidate();
            }

            protected override void OnPaintBackground(PaintEventArgs e)
            {
                UiTheme.PaintUnderlay(this, e.Graphics, ClientRectangle, UiTheme.Sidebar);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics graphics = e.Graphics;
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.PixelOffsetMode = PixelOffsetMode.Default;
                graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                using (var format = new StringFormat(StringFormat.GenericTypographic))
                using (var oldInk = new SolidBrush(ForeColor))
                using (var newInk = new SolidBrush(UiTheme.UpdateBlue))
                {
                    format.LineAlignment = StringAlignment.Center;
                    format.FormatFlags |= StringFormatFlags.NoWrap | StringFormatFlags.NoClip;
                    float oldWidth = graphics.MeasureString(current, Font, PointF.Empty, format).Width;
                    float newWidth = graphics.MeasureString(next, Font, PointF.Empty, format).Width;
                    float gap = Math.Max(2f, Height * .16f);
                    float arrowWidth = Math.Max(11.5f, Height * .73f);
                    float left = (Width - oldWidth - arrowWidth - newWidth - gap * 2f) / 2f;
                    float arrowLeft = left + oldWidth + gap;
                    float newLeft = arrowLeft + arrowWidth + gap;
                    graphics.DrawString(current, Font, oldInk, new RectangleF(left, 0, oldWidth, Height), format);
                    graphics.DrawString(next, Font, newInk, new RectangleF(newLeft, 0, newWidth, Height), format);

                    float middle = Height / 2f;
                    float tail = arrowLeft + .8f;
                    float tip = arrowLeft + arrowWidth - .8f;
                    float head = tip - Math.Max(4.4f, arrowWidth * .43f);
                    float shaft = Math.Max(1.7f, Height * .12f);
                    float wing = Math.Max(3.3f, Height * .22f);
                    using (var pen = new Pen(UiTheme.Accent, Math.Max(1f, Height * .063f)) { LineJoin = LineJoin.Round })
                    {
                        graphics.PixelOffsetMode = PixelOffsetMode.Half;
                        graphics.DrawPolygon(pen, new[]
                        {
                            new PointF(tail, middle - shaft), new PointF(head, middle - shaft),
                            new PointF(head, middle - wing), new PointF(tip, middle),
                            new PointF(head, middle + wing), new PointF(head, middle + shaft),
                            new PointF(tail, middle + shaft)
                        });
                    }
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (wheelFilter != null) Application.RemoveMessageFilter(wheelFilter);
                scrollTimer.Dispose();
                tips.Dispose();
                themeTimer.Dispose();
                currentNoticeTimer.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}

namespace QuotaTrayApp
{
    internal sealed class ReleaseUpdateDialog : UiWindow
    {
        private readonly Label detail;
        private readonly Label percent;
        private readonly UpdateTrack track;
        private readonly Button confirm;
        private Action restart;
        private bool working = true;

        internal ReleaseUpdateDialog()
        {
            Text = L.T("正在更新");
            TitlePrimary = Text;
            ShowIcon = true;
            MinimizeBox = MaximizeBox = CanResize = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(S(396), S(197));

            var card = new UiCard { Bounds = new Rectangle(S(16), S(52), S(364), S(129)) };
            Controls.Add(card);
            detail = new UiLabel
            {
                Name = "update-progress-detail", Text = L.T("正在检查更新…"),
                Bounds = new Rectangle(S(16), S(13), S(332), S(62)),
                TextAlign = ContentAlignment.TopLeft
            };
            track = new UpdateTrack { Name = "update-progress-track", Bounds = new Rectangle(S(16), S(64), S(332), S(7)) };
            percent = new UiLabel
            {
                Name = "update-progress-percent", Bounds = new Rectangle(S(16), S(79), S(332), S(20)),
                TextAlign = ContentAlignment.MiddleRight
            };
            confirm = new UiButton
            {
                Name = "update-confirm", Text = L.T("确定"), Accent = true, Visible = false,
                Bounds = new Rectangle(S(238), S(84), S(110), S(32))
            };
            confirm.Click += delegate { Close(); };
            card.Controls.Add(detail);
            card.Controls.Add(track);
            card.Controls.Add(percent);
            card.Controls.Add(confirm);
            track.BringToFront();
            percent.BringToFront();
            UiTheme.Hook(this);
        }

        internal void Post(MethodInvoker action)
        {
            try { if (IsHandleCreated && !IsDisposed) BeginInvoke(action); }
            catch (InvalidOperationException) { }
        }

        internal void SetDownloading(Version version, int progress)
        {
            detail.Text = L.F("正在下载 {0}…", version);
            track.Percent = progress;
            percent.Text = progress + "%";
        }

        internal void SetInstalling()
        {
            detail.Text = L.T("正在替换程序文件…");
            track.Percent = 100;
            percent.Text = "100%";
        }

        internal void SetComplete(Version version, Action start)
        {
            restart = start;
            ShowResult(L.T("更新已完成"), L.F("已更新到 {0}", version));
        }

        internal void SetFailure(string message)
        {
            restart = null;
            ShowResult(L.T("更新失败"), message);
        }

        private void ShowResult(string title, string message)
        {
            working = false;
            TitlePrimary = Text = title;
            detail.Text = message;
            track.Visible = percent.Visible = false;
            confirm.Visible = confirm.Enabled = true;
            AcceptButton = confirm;
            confirm.Focus();
        }

        internal void Finish()
        {
            working = false;
            restart = null;
            Close();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                if (working) e.Cancel = true;
                else if (restart != null)
                {
                    Action start = restart;
                    restart = null;
                    working = e.Cancel = true;
                    confirm.Enabled = false;
                    detail.Text = L.T("正在启动额度显示器…");
                    start();
                }
            }
            base.OnFormClosing(e);
        }

        internal sealed class UpdateTrack : Control
        {
            private int percent;
            internal UpdateTrack()
            {
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                    ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            }
            internal int Percent { set { percent = Math.Max(0, Math.Min(100, value)); Invalidate(); } }

            protected override void OnPaintBackground(PaintEventArgs e)
            {
                UiTheme.PaintUnderlay(this, e.Graphics, ClientRectangle, Parent == null ? UiTheme.Surface : Parent.BackColor);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                PaintProgress(e.Graphics, ClientRectangle, percent);
            }

            internal static void PaintProgress(Graphics graphics, Rectangle bounds, int percent)
            {
                RectangleF track = RectangleF.Inflate(bounds, -1f, -1f);
                if (track.Width <= 0 || track.Height <= 0) return;
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.PixelOffsetMode = PixelOffsetMode.Half;
                if (percent < 100) FillCapsule(graphics, track, UiTheme.Input);
                if (percent > 0)
                {
                    track.Width *= percent / 100f;
                    FillCapsule(graphics, track, UiTheme.Accent);
                }
            }

            private static void FillCapsule(Graphics graphics, RectangleF bounds, Color color)
            {
                if (bounds.Width < 1f) return;
                using (GraphicsPath shape = UiTheme.Round(bounds, bounds.Height / 2f))
                using (var brush = new LinearGradientBrush(bounds, ControlPaint.Light(color, .14f), color, 90f))
                    graphics.FillPath(brush, shape);
            }
        }
    }
}


namespace QuotaTrayApp
{
    // Shared geometry and palette for settings and its owned dialogs. Tray drawing stays separate.
    internal static class UiTheme
    {
        private static bool serifChecked;
        private static FontFamily cachedZhSerifFamily, cachedSansFamily;
        internal static FontFamily FontFamilyFor(string language = null)
        {
            string resolved = L.Guess(language ?? L.Lang);
            if (resolved == "zh")
            {
                if (!serifChecked)
                {
                    serifChecked = true;
                    foreach (string name in new[] { "Noto Serif SC", "Source Han Serif SC", "Songti SC" })
                    {
                        try
                        {
                            var family = new FontFamily(name);
                            if (family.IsStyleAvailable(FontStyle.Regular)) { cachedZhSerifFamily = family; break; }
                            family.Dispose();
                        }
                        catch { }
                    }
                }
                if (cachedZhSerifFamily != null) return cachedZhSerifFamily;
            }
            if (cachedSansFamily == null)
            {
                try { cachedSansFamily = new FontFamily("Microsoft YaHei UI"); }
                catch { cachedSansFamily = FontFamily.GenericSansSerif; }
            }
            return cachedSansFamily;
        }
        internal static Font UiFont(float size, FontStyle style = FontStyle.Regular, string language = null)
        {
            return new Font(FontFamilyFor(language), size, style);
        }

        internal const int WindowRadius = 8;
        internal const int Radius = 6;
        internal const int CardRadius = 10;
        internal const int CardInnerRadius = 9;
        private static readonly Color SelectedDangerBorder = Color.FromArgb(243, 169, 177);
        internal static string Mode = "auto";
        internal static bool Dark;
        internal static event EventHandler Changed;
        internal static Color Background { get { return Dark ? Color.FromArgb(31, 31, 31) : Color.FromArgb(244, 247, 245); } }
        internal static Color Sidebar { get { return Dark ? Color.FromArgb(38, 38, 38) : Color.FromArgb(230, 237, 232); } }
        internal static Color WarningText { get { return Dark ? Color.FromArgb(252, 154, 158) : Color.FromArgb(161, 35, 52); } }
        internal static Color UpdateBlue { get { return Dark ? Color.FromArgb(139, 190, 255) : Color.FromArgb(18, 72, 151); } }
        internal static Color Surface { get { return Dark ? Color.FromArgb(47, 47, 47) : Color.White; } }
        internal static Color Input { get { return Dark ? Color.FromArgb(57, 57, 57) : Color.FromArgb(239, 242, 246); } }
        internal static Color TextColor { get { return Dark ? Color.FromArgb(242, 242, 242) : Color.FromArgb(14, 19, 26); } }
        internal static Color Muted { get { return Dark ? Color.FromArgb(176, 182, 190) : Color.FromArgb(52, 62, 74); } }
        internal static Color Accent { get { return Dark ? Color.FromArgb(125, 221, 184) : Color.FromArgb(12, 84, 64); } }
        internal static Color Selection { get { return Dark ? Color.FromArgb(40, 68, 58) : Color.FromArgb(204, 228, 216); } }
        internal static Color Border { get { return Dark ? Color.FromArgb(70, 74, 78) : Color.FromArgb(212, 219, 226); } }
        // Account colors belong to slots a–e and stay stable when other slots are removed.
        private static readonly Color[] DarkAccounts = {
            Color.FromArgb(125, 221, 184), Color.FromArgb(123, 191, 250), Color.FromArgb(195, 164, 251),
            Color.FromArgb(240, 193, 108), Color.FromArgb(242, 155, 199)
        };
        private static readonly Color[] LightAccounts = {
            Color.FromArgb(12, 84, 64), Color.FromArgb(20, 84, 146), Color.FromArgb(98, 56, 152),
            Color.FromArgb(122, 76, 12), Color.FromArgb(136, 44, 96)
        };
        internal static int AccountNumber(string slot)
        {
            return slot != null && slot.Length == 1 && slot[0] >= 'a' && slot[0] <= 'e' ? slot[0] - 'a' + 1 : 0;
        }
        internal static Color AccountColor(string slot)
        {
            int number = AccountNumber(slot);
            return number == 0 ? Accent : (Dark ? DarkAccounts : LightAccounts)[number - 1];
        }
        internal static string Normalize(string value) { return value == "dark" || value == "light" ? value : "auto"; }
        internal static void Set(string mode)
        {
            Mode = Normalize(mode); Dark = Mode == "dark" || (Mode == "auto" && TrayContext.IsDarkMode());
            if (Changed != null) Changed(null, EventArgs.Empty);
        }
        internal static void RefreshSystem()
        {
            if (Mode == "auto" && Dark != TrayContext.IsDarkMode()) Set(Mode);
        }
        [DllImport("uxtheme.dll", CharSet=CharSet.Unicode)] private static extern int SetWindowTheme(IntPtr hwnd,string subApp,string subId);
        [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hwnd, int command);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateSolidBrush(int color);
        [DllImport("user32.dll")] private static extern int FillRect(IntPtr hdc, ref NativeRect rect, IntPtr brush);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
        [DllImport("user32.dll")] private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionData data);
        [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] private struct AccentPolicy { public int AccentState, AccentFlags; public uint GradientColor; public int AnimationId; }
        [StructLayout(LayoutKind.Sequential)] private struct WindowCompositionData { public int Attribute; public IntPtr Data; public int SizeOfData; }
        private static readonly IntPtr ZeroBrush = CreateSolidBrush(0);

        internal static float SystemDpiScale()
        {
            try { using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) return Math.Max(1f, g.DpiX / 96f); }
            catch { return 1f; }
        }
        internal static int S(int px, float scale) { return scale == 1f ? px : (int)Math.Round(px * scale); }
        internal static float DpiScaleOf(Control control)
        {
            if (control == null) return SystemDpiScale();
            UiWindow window = control.FindForm() as UiWindow;
            return window != null ? window.UiScale : SystemDpiScale();
        }

        internal static int DarkGlassBaseAlpha = 12, LightGlassBaseAlpha = 22;
        internal static int DarkSidebarGlassAlpha = 130, LightSidebarGlassAlpha = 96;
        internal static int DarkContentGlassAlpha = 32, LightContentGlassAlpha = 78;
        internal static int DarkCardGlassAlpha = 136, LightCardGlassAlpha = 62;

        internal static Color GlassMuted { get { return Dark ? Muted : Color.FromArgb(28, 36, 46); } }
        internal static Color SidebarGlassTint { get { return Dark ? Color.FromArgb(DarkSidebarGlassAlpha, 34, 36, 40) : Color.FromArgb(LightSidebarGlassAlpha, 247, 249, 250); } }
        internal static Color ContentGlassTint { get { return Dark ? Color.FromArgb(DarkContentGlassAlpha, 31, 31, 31) : Color.FromArgb(LightContentGlassAlpha, 245, 248, 249); } }
        internal static Color CardGlassColor(Color surface) { return Color.FromArgb(Dark ? DarkCardGlassAlpha : LightCardGlassAlpha, surface); }
        internal static bool IsGlass(Control control)
        {
            if (control == null) return false;
            Form form = control.FindForm();
            UiWindow window = form as UiWindow;
            if (window != null) return window.GlassEnabled;
            ToastForm toast = form as ToastForm;
            return toast != null && toast.GlassEnabled;
        }
        internal static void ClearGlass(Graphics g, Rectangle rect)
        {
            if (rect.Width <= 0 || rect.Height <= 0) return;
            IntPtr hdc = g.GetHdc();
            try
            {
                NativeRect rc = new NativeRect { Left = rect.Left, Top = rect.Top, Right = rect.Right, Bottom = rect.Bottom };
                FillRect(hdc, ref rc, ZeroBrush);
            }
            finally { g.ReleaseHdc(hdc); }
        }
        internal static void SetAcrylic(IntPtr hwnd, bool enabled)
        {
            uint a = (uint)Math.Max(0, Math.Min(255, Dark ? DarkGlassBaseAlpha : LightGlassBaseAlpha));
            // Light mode keeps RGB <= 200 in GradientColor because Win11 DWM triggers an ~85% white veil or dark cutoff above ~205.
            uint rgb = Dark ? 0x001D2028u : 0x00C8C6C4u;
            uint gradient = (a << 24) | rgb;
            AccentPolicy policy = new AccentPolicy { AccentState = enabled ? 4 : 0, AccentFlags = 0, GradientColor = enabled ? gradient : 0u };
            int size = Marshal.SizeOf(policy);
            IntPtr ptr = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(policy, ptr, false);
                WindowCompositionData data = new WindowCompositionData { Attribute = 19, Data = ptr, SizeOfData = size };
                SetWindowCompositionAttribute(hwnd, ref data);
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
            finally { Marshal.FreeHGlobal(ptr); }
        }
        private static bool IsVersionText(Control control)
        {
            return control.Name == "version-label" || control.Name == "update-version-range";
        }

        private static bool IsInSidebar(Control control)
        {
            for (Control c = control; c != null; c = c.Parent)
                if (c.Name == "sidebar" || IsVersionText(c) || c.Name == "version-update") return true;
            return false;
        }
        private static UiCard FindCardParent(Control control)
        {
            for (Control c = control == null ? null : control.Parent; c != null; c = c.Parent)
                if (c is UiCard) return (UiCard)c;
            return null;
        }
        internal static void PaintUnderlay(Control control, Graphics g, Rectangle rect, Color? opaqueFallback = null)
        {
            if (rect.Width <= 0 || rect.Height <= 0) return;
            if (!IsGlass(control))
            {
                Color fallback = opaqueFallback ?? (control != null && control.Parent != null ? control.Parent.BackColor : Background);
                using (Brush b = new SolidBrush(fallback)) g.FillRectangle(b, rect);
                return;
            }
            ClearGlass(g, rect);
            if (IsInSidebar(control))
            {
                using (Brush b = new SolidBrush(SidebarGlassTint)) g.FillRectangle(b, rect);
                return;
            }
            using (Brush content = new SolidBrush(ContentGlassTint)) g.FillRectangle(content, rect);
            UiCard card = FindCardParent(control);
            if (card != null)
            {
                Color cardFill = card.HasTextBox ? card.SurfaceColor : CardGlassColor(card.SurfaceColor);
                using (Brush b = new SolidBrush(cardFill)) g.FillRectangle(b, rect);
            }
        }
        internal static void PaintSplitGlass(UiWindow window, Graphics g, int width, int height)
        {
            Rectangle full = new Rectangle(0, 0, width, height);
            if (width <= 0 || height <= 0) return;
            if (window == null || !window.GlassEnabled)
            {
                g.Clear(Background);
                Control sb = window == null ? null : window.Controls["sidebar"];
                if (sb != null) using (Brush fill = new SolidBrush(Sidebar)) g.FillRectangle(fill, 0, 0, sb.Right, height);
                return;
            }
            ClearGlass(g, full);
            Control sidebar = window.Controls["sidebar"];
            if (sidebar != null && sidebar.Right > 0)
            {
                int split = Math.Min(width, sidebar.Right);
                using (Brush left = new SolidBrush(SidebarGlassTint)) g.FillRectangle(left, 0, 0, split, height);
                if (split < width)
                    using (Brush right = new SolidBrush(ContentGlassTint)) g.FillRectangle(right, split, 0, width - split, height);
                if (!Dark && split > 0 && split < width)
                    using (Pen hairline = new Pen(Color.FromArgb(28, 15, 23, 42))) g.DrawLine(hairline, split - 1, 0, split - 1, height);
            }
            else
            {
                using (Brush right = new SolidBrush(ContentGlassTint)) g.FillRectangle(right, full);
            }
        }
        internal static void DrawText(Control control, Graphics g, string text, Font font, Rectangle bounds, Color color, TextFormatFlags flags)
        {
            if (String.IsNullOrEmpty(text)) return;
            if (!IsGlass(control))
            {
                TextRenderer.DrawText(g, text, font, bounds, color, flags);
                return;
            }
            PixelOffsetMode previousOffset = g.PixelOffsetMode;
            g.PixelOffsetMode = PixelOffsetMode.Default;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            try
            {
                using (StringFormat fmt = new StringFormat(StringFormat.GenericTypographic))
                using (Brush brush = new SolidBrush(color))
                {
                    fmt.Alignment = (flags & TextFormatFlags.HorizontalCenter) != 0 ? StringAlignment.Center : StringAlignment.Near;
                    fmt.LineAlignment = (flags & TextFormatFlags.VerticalCenter) != 0 ? StringAlignment.Center : StringAlignment.Near;
                    fmt.Trimming = (flags & TextFormatFlags.EndEllipsis) != 0 ? StringTrimming.EllipsisCharacter : StringTrimming.None;
                    fmt.FormatFlags = (flags & TextFormatFlags.SingleLine) != 0 ? StringFormatFlags.NoWrap : 0;
                    g.DrawString(text, font, brush, bounds, fmt);
                }
            }
            finally { g.PixelOffsetMode = previousOffset; }
        }
        internal static void Present(Form form)
        {
            if (form.WindowState == FormWindowState.Minimized) form.WindowState = FormWindowState.Normal;
            form.Show();
            // STARTUPINFO SW_HIDE can hide the first WinForms Show even though Visible is true.
            ShowWindow(form.Handle, 9);
            form.BringToFront(); form.Activate();
        }
        internal static void PaintCardGlassBackground(Graphics g, Rectangle rect)
        {
            if (rect.Width <= 0 || rect.Height <= 0) return;
            ClearGlass(g, rect);
            using (Brush content = new SolidBrush(ContentGlassTint)) g.FillRectangle(content, rect);
            using (Brush card = new SolidBrush(CardGlassColor(Surface))) g.FillRectangle(card, rect);
        }
        internal static bool ApplyWindowFrame(Form form, out bool systemCorners)
        {
            systemCorners = false;
            if (form == null || !form.IsHandleCreated) return false;
            try
            {
                int dark = Dark ? 1 : 0, corner = 2;
                DwmSetWindowAttribute(form.Handle, 20, ref dark, 4);
                DwmSetWindowAttribute(form.Handle, 19, ref dark, 4);
                systemCorners = DwmSetWindowAttribute(form.Handle, 33, ref corner, 4) == 0;
                if (systemCorners)
                {
                    int borderColor = Dark ? 0x004E4A46 : 0x00C4BCB4;
                    DwmSetWindowAttribute(form.Handle, 34, ref borderColor, 4);
                }
            }
            catch (DllNotFoundException) { } catch (EntryPointNotFoundException) { }
            return systemCorners && UiBackdrop.Allowed();
        }
        private static void Frame(Form form)
        {
            if (!form.IsHandleCreated) return;
            bool systemCorners;
            bool glass = ApplyWindowFrame(form, out systemCorners);
            UiWindow window = form as UiWindow;
            if (window != null)
            {
                window.UseLocalCorners(!systemCorners);
                window.ApplyGlass(glass);
            }
        }
        internal static void RefreshGlass(Form form)
        {
            if (form == null || form.IsDisposed || !form.IsHandleCreated) return;
            UiWindow window = form as UiWindow;
            if (window != null && window.GlassEnabled) SetAcrylic(window.Handle, true);
            RefreshTree(form);
        }
        private static void RefreshTree(Control root)
        {
            root.Invalidate(true);
            foreach (Control child in root.Controls) RefreshTree(child);
        }
        internal static void ClipRoundedWindow(Form form, int radius)
        {
            using (GraphicsPath shape = Round(new RectangleF(0, 0, form.Width, form.Height), radius))
                form.Region = new Region(shape);
        }
        internal static void Hook(Form form)
        {
            EventHandler apply = delegate { if (!form.IsDisposed) { Frame(form); Apply(form); } };
            Changed += apply;
            form.HandleCreated += delegate { Frame(form); Apply(form); };
            form.Disposed += delegate { Changed -= apply; };
            Apply(form);
        }
        internal static void Apply(Control root)
        {
            bool glass = IsGlass(root);
            root.ForeColor = TextColor;
            if (root is Form) root.BackColor = glass ? Color.Black : Background;
            else if (root is Label)
            {
                ((Label)root).UseCompatibleTextRendering = glass;
                root.BackColor = glass ? Color.Transparent : (IsVersionText(root) ? Sidebar : root.Parent == null ? Background : root.Parent.BackColor);
            }
            else if (root.Name == "sidebar" || root.Name == "version-label") root.BackColor = Sidebar;
            else if (root is UiCard) root.BackColor = ((UiCard)root).SurfaceColor;
            else if (root is UiComboBox) root.BackColor = Input;
            else if (root is TextBox) root.BackColor = Surface;
            else if (root is UiButton || root is UiChoice || root is UiCheckBox || root is UiPillSlider)
            {
                UiCard card = FindCardParent(root);
                root.BackColor = card != null ? card.SurfaceColor : Surface;
            }
            else root.BackColor = root.Parent == null ? Background : root.Parent.BackColor;
            if (root.Name == "page-caption" && root.FindForm() is QuotaSettingsForm)
                root.ForeColor = Dark ? Color.FromArgb(82, 135, 116) : Color.FromArgb(54, 89, 77);
            else if (root.Name == "page-caption" || root.Name == "global-level-help" || root.Name == "global-level-error" || root.Name == "min-visible-accounts")
                root.ForeColor = glass ? GlassMuted : Muted;
            if (root.Name == "account-number") root.ForeColor = AccountColor(root.Tag as string);
            if (root.Name == "account-status-warning" || root.Name == "notify-sound-error") root.ForeColor = WarningText;
            if (IsVersionText(root)) root.ForeColor = Dark ? Color.FromArgb(142, 146, 149) : Color.FromArgb(92, 102, 108);
            if (root.Name == "update-status")
                root.ForeColor = root.Tag is bool && (bool)root.Tag ? UpdateBlue : (glass ? GlassMuted : Muted);
            if (root.Name == "reset-credits-line")
                root.ForeColor = root.Tag is bool && (bool)root.Tag ? WarningText : Accent;
            foreach (Control child in root.Controls) Apply(child);
            if (root.IsHandleCreated && (root is ScrollableControl))
            {
                try { SetWindowTheme(root.Handle,Dark ? "DarkMode_Explorer" : "Explorer",null); } catch (DllNotFoundException) { } catch (EntryPointNotFoundException) { }
            }
            root.Invalidate();
        }
        internal static GraphicsPath Round(RectangleF r, float radius)
        {
            GraphicsPath p = new GraphicsPath();
            float d = Math.Max(1, Math.Min(radius * 2, Math.Min(r.Width, r.Height)));
            p.AddArc(r.Left, r.Top, d, d, 180, 90); p.AddArc(r.Right - d, r.Top, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.Left, r.Bottom - d, d, d, 90, 90); p.CloseFigure(); return p;
        }
        internal static GraphicsPath SmoothRound(RectangleF r, float radius)
        {
            GraphicsPath p=new GraphicsPath();
            float rr=Math.Max(1,Math.Min(radius,Math.Min(r.Width,r.Height)/2f));
            float k=.5522848f; float c=rr*k;
            p.StartFigure();
            p.AddLine(r.Left+rr,r.Top,r.Right-rr,r.Top);
            p.AddBezier(r.Right-rr,r.Top,r.Right-rr+c,r.Top,r.Right,r.Top+rr-c,r.Right,r.Top+rr);
            p.AddLine(r.Right,r.Top+rr,r.Right,r.Bottom-rr);
            p.AddBezier(r.Right,r.Bottom-rr,r.Right,r.Bottom-rr+c,r.Right-rr+c,r.Bottom,r.Right-rr,r.Bottom);
            p.AddLine(r.Right-rr,r.Bottom,r.Left+rr,r.Bottom);
            p.AddBezier(r.Left+rr,r.Bottom,r.Left+rr-c,r.Bottom,r.Left,r.Bottom-rr+c,r.Left,r.Bottom-rr);
            p.AddLine(r.Left,r.Bottom-rr,r.Left,r.Top+rr);
            p.AddBezier(r.Left,r.Top+rr,r.Left,r.Top+rr-c,r.Left+rr-c,r.Top,r.Left+rr,r.Top);
            p.CloseFigure(); return p;
        }
        internal static GraphicsPath RoundCorners(RectangleF r, float topLeft, float topRight, float bottomRight, float bottomLeft)
        {
            GraphicsPath p = new GraphicsPath();
            float max = Math.Max(1f, Math.Min(r.Width, r.Height) / 2f);
            float a = Math.Min(max, Math.Max(0f, topLeft)), b = Math.Min(max, Math.Max(0f, topRight));
            float c = Math.Min(max, Math.Max(0f, bottomRight)), d = Math.Min(max, Math.Max(0f, bottomLeft));
            float x0 = r.Left, y0 = r.Top, x1 = r.Right, y1 = r.Bottom;
            if (a > 0) p.AddArc(x0, y0, a * 2, a * 2, 180, 90);
            if (b > 0) { p.AddLine(x0 + a, y0, x1 - b, y0); p.AddArc(x1 - b * 2, y0, b * 2, b * 2, 270, 90); }
            else p.AddLine(x0 + a, y0, x1, y0);
            if (c > 0) { p.AddLine(x1, y0 + b, x1, y1 - c); p.AddArc(x1 - c * 2, y1 - c * 2, c * 2, c * 2, 0, 90); }
            else p.AddLine(x1, y0 + b, x1, y1);
            if (d > 0) { p.AddLine(x1 - c, y1, x0 + d, y1); p.AddArc(x0, y1 - d * 2, d * 2, d * 2, 90, 90); }
            else p.AddLine(x1 - c, y1, x0, y1);
            p.CloseFigure();
            return p;
        }
        internal static Color QuotaInk(double remaining)
        {
            if (remaining < 0) return Dark ? Color.FromArgb(196, 204, 216) : Color.FromArgb(104, 116, 128);
            if (remaining <= 0) return Dark ? Color.FromArgb(255, 84, 94) : Color.FromArgb(148, 18, 30);
            if (remaining <= 25) return Dark ? Color.FromArgb(255, 110, 110) : Color.FromArgb(178, 44, 60);
            if (remaining < 75) return Dark ? Color.FromArgb(248, 196, 64) : Color.FromArgb(146, 98, 8);
            return Dark ? Color.FromArgb(72, 222, 142) : Color.FromArgb(18, 118, 80);
        }
        internal static void Card(Graphics g, Rectangle r, Color fill, Color border, bool gammaCorrect = true)
        {
            if(r.Width<4||r.Height<4)return;
            g.SmoothingMode=SmoothingMode.AntiAlias; g.PixelOffsetMode=PixelOffsetMode.HighQuality;
            g.CompositingQuality=gammaCorrect ? CompositingQuality.GammaCorrected : CompositingQuality.Default;
            RectangleF outer=new RectangleF(r.X+.5f,r.Y+.5f,r.Width-1,r.Height-1);
            using(GraphicsPath path=SmoothRound(outer,CardRadius))
            using(Brush surface=new SolidBrush(fill))
            using(Pen edge=new Pen(border,1f))
            {
                // One path, one antialias pass. The centered 1px stroke naturally creates the smaller inner radius.
                g.FillPath(surface,path); g.DrawPath(edge,path);
            }
        }

        internal static void Fill(Graphics g, Rectangle r, Color fill, Color? border)
        {
            PaintRoundedSurface(g, r, fill, fill, border);
        }
        internal static Color Blend(Color background, Color foreground, int alpha)
        {
            return Color.FromArgb((background.R*(255-alpha)+foreground.R*alpha)/255,(background.G*(255-alpha)+foreground.G*alpha)/255,(background.B*(255-alpha)+foreground.B*alpha)/255);
        }
        internal static void Material(Graphics g, Rectangle rect, Color background, bool accent, bool danger, bool hover, bool pressed, bool enabled, bool selected = false, Color? selectionColor = null)
        {
            if (rect.Width < 2 || rect.Height < 2) return;
            Color tint = Dark ? Color.White : Color.Black;
            Color top = Blend(background, tint, Dark ? 38 : 18);
            Color bottom = Blend(background, tint, Dark ? 16 : 7);
            Color edge = Border;
            if (accent || danger)
            {
                Color color = danger ? Color.FromArgb(143, 50, 66) : Accent;
                top = Blend(color, Color.White, Dark ? 35 : 18);
                bottom = Blend(color, Color.Black, Dark ? 12 : 24);
                top = Blend(background, top, 240);
                bottom = Blend(background, bottom, 228);
                edge = danger && selected ? SelectedDangerBorder : Blend(background, color, 195);
            }
            if (selected && !danger)
            {
                Color color = selectionColor ?? Accent;
                top = Blend(background, color, Dark ? 61 : 42);
                bottom = Blend(background, color, Dark ? 33 : 22);
                edge = color;
            }
            if (enabled && (hover || pressed))
            {
                Color overlay = pressed ? Color.Black : tint;
                top = Blend(top, overlay, pressed ? 28 : 16);
                bottom = Blend(bottom, overlay, pressed ? 25 : 10);
            }
            if (!enabled)
            {
                top = Blend(background, top, 140);
                bottom = Blend(background, bottom, 140);
                edge = Blend(background, edge, 140);
            }

            PaintRoundedSurface(g, rect, top, bottom, edge);
        }

        internal static void PaintGlassSurface(Graphics g, RectangleF rect, Color top, Color bottom, Color? border, float radius = Radius)
        {
            if (rect.Width < 2 || rect.Height < 2) return;
            RectangleF outer = new RectangleF(rect.X + 0.5f, rect.Y + 0.5f, rect.Width - 1f, rect.Height - 1f);
            float r = Math.Min(radius, Math.Min(outer.Width, outer.Height) / 2f);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            using (GraphicsPath path = SmoothRound(outer, r))
            {
                using (LinearGradientBrush brush = new LinearGradientBrush(rect, top, bottom, 90f))
                    g.FillPath(brush, path);
                if (border.HasValue && border.Value.A > 0)
                    using (Pen edge = new Pen(border.Value, 1f))
                        g.DrawPath(edge, path);
            }
        }

        internal static void LightGlassMaterial(Graphics g, Rectangle rect, bool accent, bool danger, bool hover, bool pressed, bool enabled, bool selected = false, Color? selectionColor = null)
        {
            if (rect.Width < 2 || rect.Height < 2) return;
            if (accent || danger)
            {
                Color color = danger ? Color.FromArgb(143, 50, 66) : Accent;
                Color top = Blend(color, Color.White, hover ? 22 : 12);
                Color bottom = Blend(color, Color.Black, pressed ? 28 : 18);
                Color edge = danger && selected ? SelectedDangerBorder : Blend(color, Color.Black, 26);
                int fillAlpha = enabled ? 245 : 128;
                PaintGlassSurface(g, rect, Color.FromArgb(fillAlpha, top), Color.FromArgb(fillAlpha, bottom),
                    Color.FromArgb(fillAlpha, edge));
                return;
            }
            if (selected)
            {
                Color sel = selectionColor ?? Accent;
                Color top = Color.FromArgb(hover ? 132 : 112, Blend(Color.White, sel, 78));
                Color bottom = Color.FromArgb(hover ? 108 : 88, Blend(Color.White, sel, 112));
                PaintGlassSurface(g, rect, top, bottom, Color.FromArgb(185, sel));
                return;
            }
            if (!enabled)
            {
                PaintGlassSurface(g, rect, Color.FromArgb(42, 232, 238, 244), Color.FromArgb(26, 210, 218, 226), Color.FromArgb(56, 72, 84, 96));
                return;
            }
            if (pressed)
            {
                PaintGlassSurface(g, rect, Color.FromArgb(88, 178, 194, 206), Color.FromArgb(104, 160, 178, 192), Color.FromArgb(135, 24, 38, 52));
                return;
            }
            if (hover)
            {
                PaintGlassSurface(g, rect, Color.FromArgb(96, 222, 236, 231), Color.FromArgb(74, 190, 212, 205),
                    Color.FromArgb(148, 12, 84, 64));
                return;
            }
            PaintGlassSurface(g, rect, Color.FromArgb(66, 240, 245, 249), Color.FromArgb(38, 210, 220, 230),
                Color.FromArgb(92, 42, 54, 68));
        }

        private static void PaintRoundedSurface(Graphics graphics, Rectangle rect, Color top, Color bottom, Color? border)
        {
            int width = rect.Width, height = rect.Height;
            if (width < 2 || height < 2) return;
            int radius = Math.Min(Radius, Math.Min(width, height) / 2);
            var pixels = new int[width * height];
            bool outlineOnly = top.A == 0 && bottom.A == 0;
            for (int y = 0; y < height; y++)
            {
                Color fill = outlineOnly ? Color.Transparent : Blend(top, bottom, (y * 255 + (height - 1) / 2) / (height - 1));
                Color edge = border ?? fill;
                int row = y * width, edgeY = Math.Min(y, height - 1 - y);
                int center = edgeY == 0 ? edge.ToArgb() : outlineOnly ? 0 : fill.ToArgb();
                for (int x = radius; x < width - radius; x++) pixels[row + x] = center;
                for (int x = 0; x < radius; x++)
                {
                    int color = x == 0 ? edge.ToArgb() : center;
                    if (edgeY < radius)
                    {
                        double dx = radius - x - .5, dy = radius - edgeY - .5;
                        double distance = Math.Sqrt(dx * dx + dy * dy);
                        int total = PixelCoverage(radius + .5 - distance);
                        int inside = x == 0 || edgeY == 0 ? 0 : PixelCoverage(radius - .5 - distance);
                        int rim = total - inside;
                        if (outlineOnly) inside = 0;
                        // Premultiplied pixels keep corner coverage independent of the parent color.
                        color = ((rim + inside) << 24)
                            | (((edge.R * rim + fill.R * inside + 127) / 255) << 16)
                            | (((edge.G * rim + fill.G * inside + 127) / 255) << 8)
                            | ((edge.B * rim + fill.B * inside + 127) / 255);
                    }
                    pixels[row + x] = pixels[row + width - 1 - x] = color;
                }
            }
            using (var surface = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb))
            {
                var data = surface.LockBits(new Rectangle(0, 0, width, height), System.Drawing.Imaging.ImageLockMode.WriteOnly, surface.PixelFormat);
                try { Marshal.Copy(pixels, 0, data.Scan0, pixels.Length); }
                finally { surface.UnlockBits(data); }
                graphics.DrawImageUnscaled(surface, rect.Location);
            }
        }

        private static int PixelCoverage(double distance)
        {
            return (int)Math.Round(Math.Max(0, Math.Min(1, distance)) * 255);
        }
        internal static void Focus(Graphics g, Rectangle rect)
        {
            rect.Inflate(-3,-3);
            if (rect.Width < 2 || rect.Height < 2) return;
            PaintRoundedSurface(g, rect, Color.Transparent, Color.Transparent, Muted);
        }
        internal static Color QuotaColor(double remaining)
    {
        Color healthy=Color.FromArgb(100,201,164);
        Color warning=Color.FromArgb(238,180,72);
        Color danger=Color.FromArgb(211,83,96);
        if(remaining>=75)return healthy;
        if(remaining>=60)return Lerp(healthy,warning,(float)((75-remaining)/15.0));
        if(remaining>=25)return warning;
        return Lerp(warning,danger,(float)((25-remaining)/25.0));
    }
    internal static Color Lerp(Color a, Color b, float amount)
    {
        amount=Math.Max(0,Math.Min(1,amount));
        return Color.FromArgb((int)(a.R+(b.R-a.R)*amount),(int)(a.G+(b.G-a.G)*amount),(int)(a.B+(b.B-a.B)*amount));
    }
    internal static void QuotaGradient(Graphics g, Rectangle rect, Color baseColor)
    {
        if(rect.Width<2||rect.Height<2)return;
        Color top=ControlPaint.Light(baseColor,.22f), middle=baseColor, bottom=ControlPaint.Dark(baseColor,.18f);
        RectangleF shapeRect=new RectangleF(rect.X+.5f,rect.Y+.5f,rect.Width-1,rect.Height-1);
        using(GraphicsPath shape=Round(shapeRect,Math.Min(Radius,rect.Height/2f)))
        using(LinearGradientBrush fill=new LinearGradientBrush(rect,top,bottom,90f))
        {
            ColorBlend blend=new ColorBlend(); blend.Colors=new Color[]{top,middle,bottom}; blend.Positions=new float[]{0,.48f,1}; fill.InterpolationColors=blend; g.SmoothingMode=SmoothingMode.AntiAlias; g.PixelOffsetMode=PixelOffsetMode.HighQuality; g.FillPath(fill,shape);
            using(Pen edge=new Pen(Color.FromArgb(105,255,255,255),1)) g.DrawLine(edge,rect.X+Radius,rect.Top+1,rect.Right-Radius,rect.Top+1);
            using(Pen border=new Pen(ControlPaint.Dark(baseColor,.22f),1)) g.DrawPath(border,shape);
        }
    }
    }

    internal sealed class UiMenuRenderer : ToolStripProfessionalRenderer
    {
        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e) { e.Graphics.Clear(UiTheme.Surface); }
        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
            using(GraphicsPath p=UiTheme.Round(new RectangleF(.5f,.5f,e.ToolStrip.Width-1,e.ToolStrip.Height-1),UiTheme.Radius))
            using(Pen pen=new Pen(UiTheme.Border)) e.Graphics.DrawPath(pen,p);
        }
        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if(e.Item.Selected) UiTheme.Fill(e.Graphics,new Rectangle(4,1,e.Item.Width-8,e.Item.Height-2),UiTheme.Selection,null);
        }
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e) { e.TextColor=e.Item.Enabled ? UiTheme.TextColor : UiTheme.Muted; base.OnRenderItemText(e); }
    }
    internal class UiCard : GroupBox
    {
        private Font cachedBoldFont;
        private Font cachedBoldSource;
        internal UiCard() { SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true); }
        internal string AccountSlot { get; set; }
        internal bool HasTextBox { get { foreach (Control c in Controls) if (c is TextBox) return true; return false; } }
        internal virtual Color SurfaceColor
        {
            get { return AccountSlot == null ? UiTheme.Surface : UiTheme.Blend(UiTheme.Surface, UiTheme.AccountColor(AccountSlot), UiTheme.Dark ? 16 : 12); }
        }
        protected virtual Color BorderColor
        {
            get
            {
                if (AccountSlot != null) return UiTheme.Blend(UiTheme.Border, UiTheme.AccountColor(AccountSlot), UiTheme.IsGlass(this) && !UiTheme.Dark ? 96 : 132);
                return UiTheme.IsGlass(this) && !UiTheme.Dark ? Color.FromArgb(145, 165, 178, 190) : UiTheme.Border;
            }
        }
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            if (UiTheme.IsGlass(this))
            {
                UiTheme.PaintUnderlay(this, e.Graphics, ClientRectangle);
                Color fill = HasTextBox ? SurfaceColor : UiTheme.CardGlassColor(SurfaceColor);
                UiTheme.Card(e.Graphics, ClientRectangle, fill, BorderColor, false);
                return;
            }
            e.Graphics.Clear(Parent == null ? UiTheme.Background : Parent.BackColor);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            if (!UiTheme.IsGlass(this)) UiTheme.Card(e.Graphics,ClientRectangle,SurfaceColor,BorderColor);
            if (!String.IsNullOrEmpty(Text))
            {
                if (cachedBoldFont == null || !ReferenceEquals(cachedBoldSource, Font))
                {
                    if (cachedBoldFont != null) cachedBoldFont.Dispose();
                    cachedBoldSource = Font;
                    cachedBoldFont = new Font(Font, FontStyle.Bold);
                }
                float s = UiTheme.DpiScaleOf(this);
                int inset = UiTheme.S(14, s);
                UiTheme.DrawText(this, e.Graphics, Text, cachedBoldFont,
                    new Rectangle(inset, inset, Width - inset * 2, UiTheme.S(28, s)),
                    UiTheme.TextColor, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            }
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing && cachedBoldFont != null) { cachedBoldFont.Dispose(); cachedBoldFont = null; cachedBoldSource = null; }
            base.Dispose(disposing);
        }
    }
    internal sealed class UiFlowPanel : FlowLayoutPanel
    {
        internal Func<Point> GetCustomScroll;
        internal Action<Point> SetCustomScroll;
        internal Action AfterCustomLayout;
        internal bool SuppressLayout;
        internal UiFlowPanel()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }
        public new Point AutoScrollPosition
        {
            get { return GetCustomScroll != null ? GetCustomScroll() : base.AutoScrollPosition; }
            set { if (SetCustomScroll != null) SetCustomScroll(value); else base.AutoScrollPosition = value; }
        }
        protected override void OnLayout(LayoutEventArgs levent)
        {
            if (SuppressLayout) return;
            SuppressLayout = true;
            try
            {
                base.OnLayout(levent);
                if (AfterCustomLayout != null) AfterCustomLayout();
            }
            finally { SuppressLayout = false; }
        }
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            if (UiTheme.IsGlass(this))
            {
                UiTheme.PaintUnderlay(this, e.Graphics, ClientRectangle);
                if (Name == "sidebar" && !UiTheme.Dark && Width > 0)
                    using (Pen hairline = new Pen(Color.FromArgb(28, 15, 23, 42))) e.Graphics.DrawLine(hairline, Width - 1, 0, Width - 1, Height);
            }
            else base.OnPaintBackground(e);
        }
        protected override void OnScroll(ScrollEventArgs se)
        {
            base.OnScroll(se);
            if (UiTheme.IsGlass(this)) Invalidate();
        }
    }
    internal sealed class UiScrollBar : Control
    {
        private int viewport = 1, total = 1, maxScroll;
        private double current, overscroll;
        private bool hover, dragging;
        private int dragStartY, dragTravel;
        private double dragStartScroll;
        internal event Action<double> Scrolled;
        internal event Action<int> WheelDelta;

        internal UiScrollBar()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.Hand;
        }

        internal void UpdateMetrics(int viewHeight, int totalHeight, double currentScroll, double bounceOffset, int maximum)
        {
            viewport = Math.Max(1, viewHeight);
            total = Math.Max(viewport, totalHeight);
            current = currentScroll;
            overscroll = bounceOffset;
            maxScroll = Math.Max(0, maximum);
            Invalidate();
        }

        private int BaseThumbHeight(int trackH)
        {
            float s = UiTheme.DpiScaleOf(this);
            int minH = UiTheme.S(26, s);
            double ratio = Math.Min(1.0, (double)viewport / total);
            return Math.Max(minH, (int)Math.Round(trackH * ratio));
        }

        internal Rectangle ThumbBounds()
        {
            float s = UiTheme.DpiScaleOf(this);
            int padY = UiTheme.S(2, s);
            int trackH = Math.Max(1, Height - padY * 2);
            int thumbH = BaseThumbHeight(trackH);
            int squeeze = Math.Min(Math.Max(0, thumbH - UiTheme.S(16, s)), (int)Math.Round(Math.Abs(overscroll) * 0.45));
            thumbH = Math.Max(UiTheme.S(16, s), thumbH - squeeze);
            int travel = Math.Max(0, trackH - thumbH);
            double frac = maxScroll <= 0 ? 0.0 : Math.Max(0.0, Math.Min(1.0, current / maxScroll));
            int y = padY + (int)Math.Round(travel * frac);
            if (overscroll < -0.5) y = padY;
            else if (overscroll > 0.5) y = padY + trackH - thumbH;
            int w = UiTheme.S(hover || dragging ? 6 : 4, s);
            int x = Width - w - UiTheme.S(2, s);
            return new Rectangle(Math.Max(0, x), y, w, Math.Min(trackH, thumbH));
        }

        private void BeginDragAt(int y, bool captureMouse)
        {
            if (maxScroll <= 0) return;
            float s = UiTheme.DpiScaleOf(this);
            int padY = UiTheme.S(2, s);
            int trackH = Math.Max(1, Height - padY * 2);
            int baseThumbH = BaseThumbHeight(trackH);
            int travel = Math.Max(1, trackH - baseThumbH);
            Rectangle thumb = ThumbBounds();
            dragging = true;
            dragStartY = y;
            dragTravel = travel;
            if (captureMouse) Capture = true;
            if (y >= thumb.Top && y <= thumb.Bottom)
            {
                dragStartScroll = Math.Max(0.0, Math.Min((double)maxScroll, current));
                if (Math.Abs(overscroll) > 0.01 && Scrolled != null) Scrolled(dragStartScroll);
            }
            else
            {
                double frac = Math.Max(0.0, Math.Min(1.0, (double)(y - padY - baseThumbH / 2.0) / travel));
                double target = frac * maxScroll;
                dragStartScroll = target;
                if (Scrolled != null) Scrolled(target);
            }
            Invalidate();
        }

        private void MoveDragTo(int y)
        {
            if (!dragging || maxScroll <= 0) return;
            double target = Math.Max(0.0, Math.Min((double)maxScroll, dragStartScroll + ((double)(y - dragStartY) / Math.Max(1, dragTravel)) * maxScroll));
            if (Math.Abs(target - current) < 0.25 && Math.Abs(overscroll) <= 0.01) return;
            if (Scrolled != null) Scrolled(target);
        }

        private void EndDrag(bool releaseCapture)
        {
            if (!dragging) return;
            dragging = false;
            if (releaseCapture && Capture) Capture = false;
            Invalidate();
        }

        internal void SimulateDragStartForTest(int y) { BeginDragAt(y, false); }
        internal void SimulateDragMoveForTest(int y) { MoveDragTo(y); }
        internal void SimulateDragEndForTest() { EndDrag(false); }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            BeginDragAt(e.Y, true);
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            MoveDragTo(e.Y);
        }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            EndDrag(true);
        }
        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            base.OnMouseCaptureChanged(e);
            if (!Capture && dragging) EndDrag(false);
        }
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            if (WheelDelta != null) WheelDelta(e.Delta);
            base.OnMouseWheel(e);
        }
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            UiTheme.PaintUnderlay(this, e.Graphics, ClientRectangle);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            if (total <= viewport) return;
            Rectangle thumb = ThumbBounds();
            if (thumb.Width < 2 || thumb.Height < 4) return;
            Color fill;
            if (SystemInformation.HighContrast)
                fill = dragging ? SystemColors.Highlight : SystemColors.ControlDark;
            else if (UiTheme.Dark)
                fill = dragging ? Color.FromArgb(204, UiTheme.Blend(Color.FromArgb(215, 226, 222), UiTheme.Accent, 72))
                    : Color.FromArgb(hover ? 154 : 92, 206, 218, 214);
            else
                fill = dragging ? Color.FromArgb(182, UiTheme.Blend(Color.FromArgb(46, 62, 56), UiTheme.Accent, 76))
                    : Color.FromArgb(hover ? 128 : 74, 42, 56, 52);

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            using (GraphicsPath pill = UiTheme.Round(new RectangleF(thumb.X, thumb.Y, thumb.Width, thumb.Height), thumb.Width / 2f))
            using (Brush brush = new SolidBrush(fill))
                e.Graphics.FillPath(brush, pill);
        }
    }
    internal sealed class UiPanel : Panel
    {
        internal UiPanel()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            if (UiTheme.IsGlass(this))
            {
                UiTheme.PaintUnderlay(this, e.Graphics, ClientRectangle);
                if (Name == "sidebar" && !UiTheme.Dark && Width > 0)
                    using (Pen hairline = new Pen(Color.FromArgb(28, 15, 23, 42))) e.Graphics.DrawLine(hairline, Width - 1, 0, Width - 1, Height);
            }
            else base.OnPaintBackground(e);
        }
        protected override void OnScroll(ScrollEventArgs se)
        {
            base.OnScroll(se);
            if (UiTheme.IsGlass(this)) Invalidate();
        }
    }
    internal sealed class UiLabel : Label
    {
        internal UiLabel()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            if (UiTheme.IsGlass(this)) UiTheme.PaintUnderlay(this, e.Graphics, ClientRectangle);
            else base.OnPaintBackground(e);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            if (!UiTheme.IsGlass(this)) { base.OnPaint(e); return; }
            PixelOffsetMode previousOffset = e.Graphics.PixelOffsetMode;
            e.Graphics.PixelOffsetMode = PixelOffsetMode.Default;
            e.Graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            Color color = Enabled ? ForeColor : UiTheme.Muted;
            try
            {
                using (StringFormat fmt = new StringFormat(StringFormat.GenericTypographic))
                using (Brush brush = new SolidBrush(color))
                {
                    fmt.HotkeyPrefix = UseMnemonic ? HotkeyPrefix.Show : HotkeyPrefix.None;
                    if (AutoEllipsis) fmt.Trimming = StringTrimming.EllipsisCharacter;
                    if (TextAlign == ContentAlignment.MiddleLeft || TextAlign == ContentAlignment.MiddleCenter || TextAlign == ContentAlignment.MiddleRight)
                        fmt.LineAlignment = StringAlignment.Center;
                    else if (TextAlign == ContentAlignment.BottomLeft || TextAlign == ContentAlignment.BottomCenter || TextAlign == ContentAlignment.BottomRight)
                        fmt.LineAlignment = StringAlignment.Far;
                    if (TextAlign == ContentAlignment.TopCenter || TextAlign == ContentAlignment.MiddleCenter || TextAlign == ContentAlignment.BottomCenter)
                        fmt.Alignment = StringAlignment.Center;
                    else if (TextAlign == ContentAlignment.TopRight || TextAlign == ContentAlignment.MiddleRight || TextAlign == ContentAlignment.BottomRight)
                        fmt.Alignment = StringAlignment.Far;
                    e.Graphics.DrawString(Text, Font, brush, ClientRectangle, fmt);
                }
            }
            finally { e.Graphics.PixelOffsetMode = previousOffset; }
        }
    }
    internal sealed class AlphaTextBox : TextBox
    {
        [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);
        private bool painting;
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x000F && !painting && UiTheme.IsGlass(this))
            {
                painting = true;
                try
                {
                    base.WndProc(ref m);
                    int w = Math.Max(1, ClientSize.Width), h = Math.Max(1, ClientSize.Height);
                    using (Bitmap bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb))
                    {
                        using (Graphics gb = Graphics.FromImage(bmp))
                        {
                            IntPtr hdc = gb.GetHdc();
                            try { SendMessage(Handle, 0x0318, hdc, new IntPtr(4)); }
                            finally { gb.ReleaseHdc(hdc); }
                        }
                        BitmapData bd = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
                        try
                        {
                            int n = Math.Abs(bd.Stride) * h;
                            byte[] buf = new byte[n];
                            Marshal.Copy(bd.Scan0, buf, 0, n);
                            for (int i = 3; i < n; i += 4) buf[i] = 255;
                            Marshal.Copy(buf, 0, bd.Scan0, n);
                        }
                        finally { bmp.UnlockBits(bd); }
                        using (Graphics g = Graphics.FromHwnd(Handle)) g.DrawImageUnscaled(bmp, 0, 0);
                    }
                    return;
                }
                finally { painting = false; }
            }
            base.WndProc(ref m);
        }
    }
    internal sealed class UiButton : Button
    {
        internal bool Accent, Danger;
        internal string IconKind;
        internal bool CenterIconGroup;
        private bool hover, pressed;
        internal UiButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0; Cursor = Cursors.Hand;
        }
        protected override void OnMouseEnter(EventArgs e) { hover=true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover=pressed=false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { pressed=true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { pressed=false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            UiTheme.PaintUnderlay(this, e.Graphics, ClientRectangle);
            if (UiTheme.IsGlass(this) && !UiTheme.Dark)
                UiTheme.LightGlassMaterial(e.Graphics, ClientRectangle, Accent, Danger, hover, pressed, Enabled);
            else
                UiTheme.Material(e.Graphics,ClientRectangle,BackColor,Accent,Danger,hover,pressed,Enabled);
            Color foreground = !Enabled ? UiTheme.Muted : Accent ? (UiTheme.Dark ? Color.FromArgb(14,33,42) : Color.White) : Danger ? Color.White : UiTheme.TextColor;
            Rectangle textRect = ClientRectangle;
            if (!String.IsNullOrEmpty(IconKind))
            {
                float s = UiTheme.DpiScaleOf(this);
                int iconSize = UiTheme.S(16, s), gap = UiTheme.S(6, s), pad = UiTheme.S(12, s);
                Size textSize = TextRenderer.MeasureText(Text, Font, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
                int groupWidth = iconSize + gap + textSize.Width;
                int iconLeft = CenterIconGroup ? Math.Max(pad, (Width - groupWidth) / 2) : pad;
                UiSymbols.Action(e.Graphics, IconKind, new Rectangle(iconLeft, (Height - iconSize) / 2, iconSize, iconSize), foreground);
                textRect = new Rectangle(iconLeft + iconSize + gap, 0, Math.Max(1, Width - iconLeft - iconSize - gap - gap), Height);
                UiTheme.DrawText(this,e.Graphics,Text,Font,textRect,foreground,TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            }
            else UiTheme.DrawText(this,e.Graphics,Text,Font,textRect,foreground,TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            if(Focused && ShowFocusCues && UiInput.Keyboard) UiTheme.Focus(e.Graphics,ClientRectangle);
        }
        internal Rectangle IconBounds()
        {
            if (String.IsNullOrEmpty(IconKind)) return Rectangle.Empty;
            float s = UiTheme.DpiScaleOf(this);
            int iconSize = UiTheme.S(16, s), gap = UiTheme.S(6, s), pad = UiTheme.S(12, s);
            Size textSize = TextRenderer.MeasureText(Text, Font, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
            int groupWidth = iconSize + gap + textSize.Width;
            int iconLeft = CenterIconGroup ? Math.Max(pad, (Width - groupWidth) / 2) : pad;
            return new Rectangle(iconLeft, (Height - iconSize) / 2, iconSize, iconSize);
        }
        internal Rectangle TextBounds()
        {
            Rectangle icon = IconBounds();
            if (icon == Rectangle.Empty) return ClientRectangle;
            Size textSize = TextRenderer.MeasureText(Text, Font, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
            return new Rectangle(icon.Right + 6, (Height - Font.Height) / 2, textSize.Width, Font.Height);
        }
    }
    internal sealed class UiChoice : RadioButton
    {
        internal string AccountSlot { get; set; }
        internal bool Danger { get; set; }
        private bool hover, pressed;

        internal UiChoice()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.Hand;
        }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = pressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { pressed = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnCheckedChanged(EventArgs e) { Invalidate(); base.OnCheckedChanged(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            UiTheme.PaintUnderlay(this, e.Graphics, ClientRectangle, BackColor);
            if (UiTheme.IsGlass(this) && !UiTheme.Dark)
                UiTheme.LightGlassMaterial(e.Graphics, ClientRectangle, false, Danger, hover, pressed, Enabled, Checked, UiTheme.AccountColor(AccountSlot));
            else
                UiTheme.Material(e.Graphics, ClientRectangle, BackColor, false, Danger, hover, pressed, Enabled, Checked, UiTheme.AccountColor(AccountSlot));
            Rectangle text = ClientRectangle;
            text.Inflate(-8, 0);
            Color foreground = !Enabled ? UiTheme.Muted : Danger ? Color.White : UiTheme.TextColor;
            UiTheme.DrawText(this, e.Graphics, Text, Font, text, foreground,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            if (Focused && ShowFocusCues && UiInput.Keyboard) UiTheme.Focus(e.Graphics, ClientRectangle);
        }
    }

    internal sealed class UiNavigation : RadioButton
    {
        private bool hover;
        internal UiNavigation() { SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true); Cursor=Cursors.Hand; }
        protected override void OnMouseEnter(EventArgs e) { hover=true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover=false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnCheckedChanged(EventArgs e) { Invalidate(); base.OnCheckedChanged(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            UiTheme.PaintUnderlay(this, e.Graphics, ClientRectangle);
            bool lightGlass = UiTheme.IsGlass(this) && !UiTheme.Dark;
            if(Checked || hover)
            {
                if (lightGlass)
                {
                    if (Checked)
                        UiTheme.PaintGlassSurface(e.Graphics, ClientRectangle,
                            Color.FromArgb(hover ? 136 : 114, 198, 229, 215),
                            Color.FromArgb(hover ? 112 : 90, 174, 214, 196),
                            Color.FromArgb(105, 12, 84, 64));
                    else
                        UiTheme.PaintGlassSurface(e.Graphics, ClientRectangle,
                            Color.FromArgb(28, 15, 23, 42), Color.FromArgb(42, 15, 23, 42),
                            Color.FromArgb(52, 15, 23, 42));
                }
                else
                {
                    Color pill = Checked ? UiTheme.Selection : UiTheme.Input;
                    if (UiTheme.IsGlass(this)) pill = Color.FromArgb(Checked ? 215 : 175, pill);
                    UiTheme.Fill(e.Graphics,ClientRectangle,pill,null);
                }
            }
            Color iconColor = Checked ? UiTheme.Accent : (lightGlass ? Color.FromArgb(24, 32, 42) : UiTheme.Muted);
            float s = UiTheme.DpiScaleOf(this);
            int iconLeft = UiTheme.S(9, s), iconSize = UiTheme.S(16, s), textLeft = UiTheme.S(30, s);
            UiSymbols.Navigation(e.Graphics,Name,new Rectangle(iconLeft,(Height-iconSize)/2,iconSize,iconSize),iconColor);
            UiTheme.DrawText(this,e.Graphics,Text,Font,new Rectangle(textLeft,0,Width-textLeft-UiTheme.S(6, s),Height),Checked ? UiTheme.Accent : UiTheme.TextColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
            if(Focused && ShowFocusCues && UiInput.Keyboard) UiTheme.Focus(e.Graphics,ClientRectangle);
        }
    }
    internal sealed class UiCheckBox : CheckBox
    {
        private readonly System.Windows.Forms.Timer timer=new System.Windows.Forms.Timer();
        private float position,from,to;private DateTime start;
        internal UiCheckBox()
        {
            SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);
            timer.Interval=15;timer.Tick+=delegate {float t=Math.Min(1,(float)(DateTime.UtcNow-start).TotalMilliseconds/180);position=from+(to-from)*(1-(float)Math.Pow(1-t,3));Invalidate();if(t>=1)timer.Stop();};
        }
        public override Size GetPreferredSize(Size proposedSize){Size sz=TextRenderer.MeasureText(Text,Font); float s=UiTheme.DpiScaleOf(this); return new Size(sz.Width+UiTheme.S(54,s),Math.Max(UiTheme.S(29,s),sz.Height+UiTheme.S(7,s)));}
        protected override void OnCheckedChanged(EventArgs e)
        {
            from=position;to=Checked?1:0;start=DateTime.UtcNow;if(IsHandleCreated)timer.Start();else position=to;base.OnCheckedChanged(e);Invalidate();
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            UiTheme.PaintUnderlay(this, e.Graphics, ClientRectangle);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            float s = UiTheme.DpiScaleOf(this);
            int tw = UiTheme.S(36, s), th = UiTheme.S(20, s), pad = UiTheme.S(3, s), ks = UiTheme.S(14, s), textLeft = UiTheme.S(48, s);
            // The track starts on the control's left edge so every switch lines up with the
            // card content guide (dropdowns, sliders, headings) instead of sitting 3 DIP in.
            Rectangle track = new Rectangle(0, (Height - th) / 2, tw, th);
            Color accent = UiTheme.AccountColor(Tag as string);
            Color offTop, offBottom, offEdge, onTop, onBottom, onEdge;
            if (UiTheme.Dark)
            {
                Color off = UiTheme.Blend(BackColor, Color.White, Enabled ? 47 : 25);
                offTop = ControlPaint.Light(off, .10f);
                offBottom = off;
                offEdge = Color.FromArgb(Enabled ? 190 : 120, UiTheme.Border);
                Color on = UiTheme.Blend(BackColor, accent, Enabled ? 210 : 105);
                onTop = ControlPaint.Light(on, .10f);
                onBottom = on;
                onEdge = Color.FromArgb(Enabled ? 160 : 88, UiTheme.Blend(on, Color.Black, 38));
            }
            else
            {
                offTop = Color.FromArgb(Enabled ? 128 : 78, 178, 190, 200);
                offBottom = Color.FromArgb(Enabled ? 152 : 90, 148, 162, 176);
                offEdge = Color.FromArgb(Enabled ? 115 : 64, 42, 54, 68);
                onTop = Color.FromArgb(Enabled ? 242 : 136, UiTheme.Blend(accent, Color.White, 16));
                onBottom = Color.FromArgb(Enabled ? 242 : 136, UiTheme.Blend(accent, Color.Black, 16));
                onEdge = Color.FromArgb(Enabled ? 210 : 112, UiTheme.Blend(accent, Color.Black, 28));
            }
            UiTheme.PaintGlassSurface(e.Graphics, track, BlendArgb(offTop, onTop, position),
                BlendArgb(offBottom, onBottom, position), BlendArgb(offEdge, onEdge, position), th / 2f);
            Rectangle knob = new Rectangle(track.Left + pad + (int)Math.Round(position * Math.Max(1, tw - ks - pad * 2)), track.Top + pad, ks, ks);
            using (Brush shadow = new SolidBrush(Color.FromArgb(Enabled ? 60 : 30, 0, 0, 0)))
                e.Graphics.FillEllipse(shadow, knob.X, knob.Y + 1, knob.Width, knob.Height);
            Color knobBottom = UiTheme.Dark ? Color.FromArgb(219, 224, 229) : Color.FromArgb(236, 240, 244);
            using (LinearGradientBrush brush = new LinearGradientBrush(knob, Color.White, knobBottom, 90))
                e.Graphics.FillEllipse(brush, knob);
            UiTheme.DrawText(this, e.Graphics, Text, Font, new Rectangle(textLeft, 0, Width - textLeft, Height),
                Enabled ? UiTheme.TextColor : UiTheme.Muted, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            if (Focused && UiInput.Keyboard) UiTheme.Focus(e.Graphics, ClientRectangle);
        }
        private static Color BlendArgb(Color a, Color b, float t)
        {
            int k = (int)Math.Round(Math.Max(0f, Math.Min(1f, t)) * 255f);
            return Color.FromArgb((a.A * (255 - k) + b.A * k) / 255, (a.R * (255 - k) + b.R * k) / 255,
                (a.G * (255 - k) + b.G * k) / 255, (a.B * (255 - k) + b.B * k) / 255);
        }
        protected override void Dispose(bool disposing){if(disposing)timer.Dispose();base.Dispose(disposing);}
    }
    internal sealed class UiPillSlider : Control
    {
        private const int SettleMilliseconds = 95;
        private const double DetentHold = 6.6, DetentReach = 16.5, ReleaseReach = 9.9;
        private readonly int[] stops;
        private readonly string[] labels;
        private readonly Timer settle = new Timer { Interval = 15 };
        private readonly System.Diagnostics.Stopwatch elapsed = new System.Diagnostics.Stopwatch();
        private double rawValue, position, animationStart;
        private float grabOffset;
        private int value;
        private bool hover, dragging;
        internal event EventHandler ValueChanged, DragCompleted;

        internal UiPillSlider(int[] stops, string[] labels)
        {
            this.stops = stops;
            this.labels = labels;
            AccessibleRole = AccessibleRole.Slider;
            TabStop = true;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            settle.Tick += delegate
            {
                double t = Math.Min(1.0, elapsed.Elapsed.TotalMilliseconds / SettleMilliseconds);
                position = value + (animationStart - value) * Math.Pow(1 - t, 3);
                Invalidate();
                if (t >= 1) settle.Stop();
            };
        }

        internal int Value
        {
            get { return value; }
            set
            {
                int next = Math.Max(0, Math.Min(100, value));
                if (!dragging)
                {
                    settle.Stop();
                    position = next;
                }
                Invalidate();
                if (next == this.value) return;
                this.value = next;
                if (ValueChanged != null) ValueChanged(this, EventArgs.Empty);
                if (IsHandleCreated && !IsDisposed) AccessibilityNotifyClients(AccessibleEvents.ValueChange, -1);
            }
        }
        internal bool IsDragging { get { return dragging; } }

        private int D(int pixels) { return UiTheme.S(pixels, UiTheme.DpiScaleOf(this)); }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && Enabled)
            {
                Focus();
                settle.Stop();
                float distance = e.X - ValueX(position);
                grabOffset = Math.Abs(distance) <= D(9) && Math.Abs(e.Y - D(16)) <= D(13) ? distance : 0;
                dragging = true;
                Capture = true;
                MoveThumb(e.X);
            }
            base.OnMouseDown(e);
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (dragging) MoveThumb(e.X);
            base.OnMouseMove(e);
        }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && dragging)
            {
                MoveThumb(e.X);
                FinishDrag();
            }
            base.OnMouseUp(e);
        }
        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            if (!Capture && dragging) FinishDrag();
            base.OnMouseCaptureChanged(e);
        }

        private int ClosestStop(double target)
        {
            int closest = stops[0];
            foreach (int stop in stops)
                if (Math.Abs(stop - target) < Math.Abs(closest - target)) closest = stop;
            return closest;
        }
        private void MoveThumb(int pointerX)
        {
            float start = ValueX(0), span = ValueX(100) - start;
            rawValue = Math.Max(0, Math.Min(100, (pointerX - grabOffset - start) * 100.0 / span));
            int stop = ClosestStop(rawValue);
            double distance = Math.Abs(rawValue - stop) * span / (100 * UiTheme.DpiScaleOf(this));
            // Blend smoothly from a short hold at the mark to unrestricted pointer movement.
            double t = Math.Max(0, Math.Min(1, (distance - DetentHold) / (DetentReach - DetentHold)));
            position = stop + (rawValue - stop) * t * t * (3 - 2 * t);
            Value = (int)Math.Round(position);
        }
        private void FinishDrag()
        {
            int stop = ClosestStop(rawValue);
            if (Math.Abs(ValueX(rawValue) - ValueX(stop)) <= ReleaseReach * UiTheme.DpiScaleOf(this)) Value = stop;
            dragging = false;
            Capture = false;
            if (Math.Abs(position - value) > .1 && SystemInformation.IsMenuAnimationEnabled)
            {
                animationStart = position;
                elapsed.Restart();
                settle.Start();
            }
            else position = value;
            Invalidate();
            if (DragCompleted != null) DragCompleted(this, EventArgs.Empty);
        }

        protected override bool IsInputKey(Keys keyData)
        {
            switch (keyData & Keys.KeyCode)
            {
                case Keys.Left: case Keys.Right: case Keys.Up: case Keys.Down:
                case Keys.PageUp: case Keys.PageDown: case Keys.Home: case Keys.End: return true;
                default: return base.IsInputKey(keyData);
            }
        }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (!Enabled) return;
            switch (e.KeyCode)
            {
                case Keys.Left: case Keys.Down: Value--; break;
                case Keys.Right: case Keys.Up: Value++; break;
                case Keys.PageDown: Value -= 10; break;
                case Keys.PageUp: Value += 10; break;
                case Keys.Home: Value = 0; break;
                case Keys.End: Value = 100; break;
                default: base.OnKeyDown(e); return;
            }
            e.Handled = true;
            base.OnKeyDown(e);
        }

        protected override AccessibleObject CreateAccessibilityInstance() { return new SliderAccessible(this); }
        private sealed class SliderAccessible : ControlAccessibleObject
        {
            private readonly UiPillSlider slider;
            internal SliderAccessible(UiPillSlider slider) : base(slider) { this.slider = slider; }
            public override string Value
            {
                get { return slider.Value + "%"; }
                set
                {
                    int number;
                    if (slider.Enabled && value != null && Int32.TryParse(value.Trim().TrimEnd('%'), out number))
                        slider.Value = number;
                }
            }
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) { dragging = false; settle.Dispose(); }
            base.Dispose(disposing);
        }
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            UiTheme.PaintUnderlay(this, e.Graphics, ClientRectangle, BackColor);
        }

        internal Rectangle ChannelBounds()
        {
            int inset = D(16);
            return new Rectangle(inset, D(13), Math.Max(1, Width - inset * 2), D(6));
        }
        internal float ValueX(double value)
        {
            Rectangle rail = ChannelBounds();
            int halfThumb = D(5);
            return rail.Left + halfThumb + (float)(Math.Max(1, rail.Width - halfThumb * 2) * value / 100);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (Width < 2 || Height < 2) return;
            float scale = UiTheme.DpiScaleOf(this);
            Rectangle rail = ChannelBounds();
            int center = D(16);
            int thumbWidth = UiTheme.S(10, scale), thumbHeight = UiTheme.S(18, scale);
            float x = ValueX(position);
            RectangleF thumb = new RectangleF(x - thumbWidth / 2f, center - thumbHeight / 2f, thumbWidth, thumbHeight);
            if (SystemInformation.HighContrast)
            {
                Color state = Enabled ? SystemColors.Highlight : SystemColors.GrayText;
                PaintRail(e.Graphics, rail, x, SystemColors.Window, SystemColors.Window, SystemColors.WindowText,
                    state, state);
                UiTheme.PaintGlassSurface(e.Graphics, thumb, state, state, SystemColors.WindowText, UiTheme.S(4, scale));
            }
            else
            {
                bool dark = UiTheme.Dark;
                Color accent = Enabled ? UiTheme.Accent : Color.FromArgb(128, 128, 128);
                Color railTop = Tint(dark ? 158 : 112, dark ? Color.FromArgb(20, 25, 27) : Color.FromArgb(105, 124, 119));
                Color railBottom = Tint(dark ? 86 : 51, dark ? Color.FromArgb(106, 118, 116) : Color.FromArgb(159, 180, 172));
                Color railEdge = Tint(dark ? 60 : 64, dark ? Color.White : Color.FromArgb(71, 99, 89));
                PaintRail(e.Graphics, rail, x, railTop, railBottom, railEdge,
                    Tint(219, UiTheme.Blend(accent, Color.White, dark ? 81 : 86)),
                    Tint(179, UiTheme.Blend(accent, Color.Black, dark ? 52 : 22)));

                int radius = UiTheme.S(4, scale);
                RectangleF shadow = thumb;
                shadow.Offset(0, UiTheme.S(1, scale));
                UiTheme.PaintGlassSurface(e.Graphics, shadow, Tint(74, Color.Black), Tint(24, Color.Black), null, radius);
                int light = dragging && Math.Abs(position - ClosestStop(position)) < .05 ? 63 : hover || dragging ? 38 : 25;
                Color front = UiTheme.Blend(dark ? Color.FromArgb(131, 171, 158) : Color.FromArgb(238, 249, 245), Color.White, light);
                Color baseColor = dark ? Color.FromArgb(39, 75, 64) : Color.FromArgb(144, 193, 174);
                if (!Enabled)
                {
                    int upperGray = dark ? 143 : 192, lowerGray = dark ? 74 : 133;
                    front = Color.FromArgb(upperGray, upperGray, upperGray);
                    baseColor = Color.FromArgb(lowerGray, lowerGray, lowerGray);
                }
                UiTheme.PaintGlassSurface(e.Graphics, thumb, Tint(231, front), Tint(204, baseColor),
                    Tint(235, UiTheme.Blend(accent, Color.White, dark ? 54 : 36)), radius);
                RectangleF shine = RectangleF.Inflate(thumb, -UiTheme.S(2, scale), -UiTheme.S(2, scale));
                shine.Height = UiTheme.S(7, scale);
                UiTheme.PaintGlassSurface(e.Graphics, shine, Tint(147, Color.White), Tint(0, Color.White), null, UiTheme.S(2, scale));
            }
            PaintScale(e.Graphics);
            if (Focused && ShowFocusCues && UiInput.Keyboard) UiTheme.Focus(e.Graphics, ClientRectangle);
        }

        private Color Tint(int opacity, Color color) { return Color.FromArgb(Enabled ? opacity : opacity * 55 / 100, color); }

        private void PaintRail(Graphics graphics, RectangleF bounds, float thumbX,
            Color top, Color bottom, Color edge, Color filledTop, Color filledBottom)
        {
            UiTheme.PaintGlassSurface(graphics, bounds, top, bottom, edge, bounds.Height / 2);
            if (position <= 0) return;
            float width = position >= 100 ? bounds.Width : thumbX - bounds.Left;
            if (width < 2) return;
            bounds.Width = width;
            UiTheme.PaintGlassSurface(graphics, bounds, filledTop, filledBottom, null, bounds.Height / 2);
        }
        internal Rectangle ScaleCaptionBounds(int index)
        {
            int x = (int)Math.Round(ValueX(stops[index]));
            int width = TextRenderer.MeasureText(labels[index], Font).Width;
            return new Rectangle(x - width / 2, D(34), width, Height - D(34));
        }
        private void PaintScale(Graphics graphics)
        {
            Color normal = SystemInformation.HighContrast ? SystemColors.WindowText : UiTheme.IsGlass(this) ? UiTheme.GlassMuted : UiTheme.Muted;
            Color active = SystemInformation.HighContrast ? SystemColors.Highlight : UiTheme.Accent;
            if (!Enabled)
                normal = SystemInformation.HighContrast ? SystemColors.GrayText :
                    UiTheme.IsGlass(this) ? Color.FromArgb(145, normal) : UiTheme.Blend(BackColor, normal, 145);
            int top = D(29), bottom = D(32);
            for (int i = 0; i < labels.Length; i++)
            {
                float x = ValueX(stops[i]);
                bool selected = Enabled && Value == stops[i];
                Color ink = selected ? active : normal;
                using (var pen = new Pen(Color.FromArgb(selected ? 225 : 146, ink)))
                    graphics.DrawLine(pen, x, top, x, bottom);
                UiTheme.DrawText(this, graphics, labels[i], Font, ScaleCaptionBounds(i), ink,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.SingleLine);
            }
        }
    }

    internal sealed class QuotaMeter : Control
    {
        private WindowInfo quota;
        private string title, reset, resetClock;
        private bool superseded;
        private Font largeFont;
        private FontFamily largeFontFamily;
        internal static bool Valid(WindowInfo w) { return w!=null && !Double.IsNaN(w.used) && !Double.IsInfinity(w.used) && w.used>=0 && w.used<=100 && w.windowMins>0; }
        // 设置页空间够用，倒计时后面补一条具体日期时间，免得机主要自己算到哪天几点。
        internal static string ResetClock(double unixSec)
        {
            if (Double.IsNaN(unixSec) || Double.IsInfinity(unixSec) || unixSec <= 0 || unixSec > 253402300799) return "";
            DateTime at = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(unixSec).ToLocalTime();
            string clock = at.ToString("HH:mm");
            if (at.Year != DateTime.Now.Year) return L.F("（{0}年{1}月{2}日 {3}）", at.Year, at.Month, at.Day, clock);
            return L.F("（{0}月{1}日 {2}）", at.Month, at.Day, clock);
        }
        internal string ResetClockForTest { get { return resetClock; } }
        internal QuotaMeter(WindowInfo quota, string title, bool superseded = false)
        {
            float s=UiTheme.SystemDpiScale();
            Name="quota-meter"; Size=new Size(UiTheme.S(420,s),UiTheme.S(92,s)); Margin=new Padding(0);
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw,true);
            AccessibleRole = AccessibleRole.Graphic;
            UpdateQuota(quota, title, superseded);
        }

        internal void UpdateQuota(WindowInfo window, string label, bool superseded = false)
        {
            if (!Valid(window)) window = null;
            bool nextSuperseded = window != null && superseded;
            string nextReset = window == null ? L.T("暂无") : TrayContext.MultiTooltipCountdown(window.resetsAt);
            string nextClock = window == null ? "" : ResetClock(window.resetsAt);
            if (title == label && reset == nextReset && resetClock == nextClock && this.superseded == nextSuperseded &&
                (quota == null ? window == null : window != null && quota.used == window.used)) return;
            quota = window; title = label; reset = nextReset; resetClock = nextClock; this.superseded = nextSuperseded;
            Text = title + " · " + (quota == null ? L.T("暂无") : L.F("剩余 {0}%", QuotaWindow.Remaining(quota.used)));
            AccessibleName = String.IsNullOrEmpty(resetClock) ? Text : Text + " · " + reset + " · " + resetClock;
            Invalidate();
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            UiTheme.PaintUnderlay(this, e.Graphics, ClientRectangle, Parent == null ? UiTheme.Surface : Parent.BackColor);
            float s=UiTheme.DpiScaleOf(this);
            int remaining=quota==null ? 0 : QuotaWindow.Remaining(quota.used);
            Color color = quota == null ? UiTheme.Muted : superseded ? (UiTheme.Dark ? Color.FromArgb(102, 110, 122) : Color.FromArgb(158, 168, 178)) : UiTheme.QuotaColor(remaining);
            Color mutedInk = superseded ? (UiTheme.Dark ? Color.FromArgb(128, 136, 148) : Color.FromArgb(106, 114, 124)) : (UiTheme.IsGlass(this) ? UiTheme.GlassMuted : UiTheme.Muted);
            UiTheme.DrawText(this,e.Graphics,title+" · "+L.T("剩余"),Font,new Rectangle(0,0,Width,UiTheme.S(22,s)),mutedInk,TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            string amount=quota==null ? "—" : remaining+"%";
            if (largeFont == null || !Equals(largeFontFamily, Font.FontFamily))
            {
                if (largeFont != null) largeFont.Dispose();
                largeFontFamily = Font.FontFamily;
                largeFont = new Font(largeFontFamily, 22f, FontStyle.Bold);
            }
            Rectangle amountRect = new Rectangle(0, UiTheme.S(19, s), Width, UiTheme.S(40, s));
            Color amountColor = superseded ? mutedInk : (quota != null && remaining <= 0 ? UiTheme.QuotaInk(0) : UiTheme.TextColor);
            UiTheme.DrawText(this, e.Graphics, amount, largeFont, amountRect, amountColor, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            if (superseded && quota != null)
            {
                float textWidth;
                if (UiTheme.IsGlass(this))
                    using (var fmt = new StringFormat(StringFormat.GenericTypographic))
                        textWidth = e.Graphics.MeasureString(amount, largeFont, PointF.Empty, fmt).Width;
                else
                    textWidth = TextRenderer.MeasureText(e.Graphics, amount, largeFont, amountRect.Size, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding).Width;
                float strikeY = (float)Math.Round(amountRect.Top + amountRect.Height * 0.50f) + 0.5f;
                Color strikeColor = UiTheme.Dark ? Color.FromArgb(215, 144, 152, 164) : Color.FromArgb(210, 92, 100, 110);
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (var strikePen = new Pen(strikeColor, Math.Max(1.8f, 2f * s)))
                    e.Graphics.DrawLine(strikePen, amountRect.Left, strikeY, Math.Min(amountRect.Right, amountRect.Left + textWidth + UiTheme.S(2, s)), strikeY);
            }
            Rectangle track=new Rectangle(0,UiTheme.S(61,s),Width,Math.Max(4,UiTheme.S(5,s))); UiTheme.QuotaGradient(e.Graphics,track,UiTheme.Input);
            if(quota!=null) { int filled=(int)Math.Round(track.Width*remaining/100.0); if(filled>1) UiTheme.QuotaGradient(e.Graphics,new Rectangle(track.X,track.Y,filled,track.Height),color); }
            Rectangle resetRow = new Rectangle(0, UiTheme.S(69, s), Width, UiTheme.S(22, s));
            int clockWidth = 0;
            if (!String.IsNullOrEmpty(resetClock))
                clockWidth = TextRenderer.MeasureText(e.Graphics, resetClock, Font, new Size(Width, resetRow.Height),
                    TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix).Width;
            int clockLeft = Math.Max(0, resetRow.Width - clockWidth);
            int countWidth = clockWidth > 0 ? Math.Max(1, clockLeft - UiTheme.S(8, s)) : resetRow.Width;
            UiTheme.DrawText(this, e.Graphics, reset, Font, new Rectangle(resetRow.Left, resetRow.Top, countWidth, resetRow.Height), mutedInk,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            if (clockWidth > 0)
                UiTheme.DrawText(this, e.Graphics, resetClock, Font, new Rectangle(clockLeft, resetRow.Top, clockWidth, resetRow.Height), mutedInk,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing && largeFont != null) { largeFont.Dispose(); largeFont = null; largeFontFamily = null; }
            base.Dispose(disposing);
        }
    }
}


namespace QuotaTrayApp
{
    internal sealed class UiInput : IMessageFilter
    {
        internal static bool Keyboard;
        private static bool installed;
        internal static void Install() { if(!installed) { Application.AddMessageFilter(new UiInput()); installed=true; } }
        public bool PreFilterMessage(ref Message m)
        {
            if(m.Msg==0x100 || m.Msg==0x104) Keyboard=true;
            if(m.Msg==0x201 || m.Msg==0x204) Keyboard=false;
            return false;
        }
    }
    internal static class UiSymbols
    {
        private static float S(float v, float scale) { return v * scale; }
        internal static void Action(Graphics g, string kind, Rectangle r, Color color)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float sc = r.Width / 16f;
            using (Pen p = new Pen(color, Math.Max(1.4f, 1.6f * sc)))
            {
                if (kind == "shortcut")
                {
                    g.DrawRectangle(p, r.X + S(1, sc), r.Y + S(2, sc), S(12, sc), S(9, sc));
                    g.DrawLine(p, r.X + S(4, sc), r.Y + S(14, sc), r.X + S(10, sc), r.Y + S(14, sc));
                    g.DrawLine(p, r.X + S(7, sc), r.Y + S(11, sc), r.X + S(7, sc), r.Y + S(14, sc));
                    g.DrawLine(p, r.X + S(10, sc), r.Y + S(3, sc), r.X + S(14, sc), r.Y + S(3, sc));
                    g.DrawLine(p, r.X + S(14, sc), r.Y + S(3, sc), r.X + S(14, sc), r.Y + S(7, sc));
                    g.DrawLine(p, r.X + S(14, sc), r.Y + S(3, sc), r.X + S(10, sc), r.Y + S(7, sc));
                }
                else if (kind == "codex")
                {
                    g.DrawRectangle(p, r.X + S(1, sc), r.Y + S(1, sc), S(13, sc), S(13, sc));
                    g.DrawLine(p, r.X + S(4, sc), r.Y + S(7, sc), r.X + S(6, sc), r.Y + S(9, sc));
                    g.DrawLine(p, r.X + S(6, sc), r.Y + S(9, sc), r.X + S(10, sc), r.Y + S(5, sc));
                }
                else
                {
                    g.DrawRectangle(p, r.X + S(5, sc), r.Y + S(1, sc), S(9, sc), S(13, sc));
                    g.DrawLine(p, r.X + S(1, sc), r.Y + S(8, sc), r.X + S(9, sc), r.Y + S(8, sc));
                    g.DrawLine(p, r.X + S(6, sc), r.Y + S(5, sc), r.X + S(9, sc), r.Y + S(8, sc));
                    g.DrawLine(p, r.X + S(6, sc), r.Y + S(11, sc), r.X + S(9, sc), r.Y + S(8, sc));
                }
            }
        }
        internal static void Navigation(Graphics g,string name,Rectangle r,Color color)
        {
            g.SmoothingMode=SmoothingMode.AntiAlias;
            float sc = r.Width / 16f;
            using(Pen p=new Pen(color, Math.Max(1.4f, 1.6f * sc)))
            {
                float x=r.X,y=r.Y,w=r.Width;
                if(name.Contains("accounts")) { g.DrawEllipse(p,x+S(4,sc),y+S(1,sc),S(8,sc),S(8,sc)); g.DrawArc(p,x+S(1,sc),y+S(9,sc),S(14,sc),S(9,sc),180,180); }
                else if(name.Contains("display")) { g.DrawRectangle(p,x+S(1,sc),y+S(2,sc),w-S(2,sc),S(10,sc)); g.DrawLine(p,x+S(8,sc),y+S(12,sc),x+S(8,sc),y+S(15,sc)); g.DrawLine(p,x+S(4,sc),y+S(15,sc),x+S(12,sc),y+S(15,sc)); }
                else if(name.Contains("notifications")) { g.DrawArc(p,x+S(4,sc),y+S(1,sc),S(8,sc),S(10,sc),180,180); g.DrawLine(p,x+S(4,sc),y+S(6,sc),x+S(2,sc),y+S(12,sc)); g.DrawLine(p,x+S(12,sc),y+S(6,sc),x+S(14,sc),y+S(12,sc)); g.DrawLine(p,x+S(2,sc),y+S(12,sc),x+S(14,sc),y+S(12,sc)); g.DrawArc(p,x+S(6,sc),y+S(12,sc),S(4,sc),S(4,sc),0,180); }
                else if(name.Contains("refresh")) { g.DrawArc(p,x+S(2,sc),y+S(2,sc),S(12,sc),S(12,sc),30,290); g.DrawLines(p,new PointF[] {new PointF(x+S(11,sc),y),new PointF(x+S(14,sc),y+S(4,sc)),new PointF(x+S(9,sc),y+S(4,sc))}); }
                else { g.DrawEllipse(p,x+S(3,sc),y+S(3,sc),S(10,sc),S(10,sc)); g.DrawEllipse(p,x+S(6,sc),y+S(6,sc),S(4,sc),S(4,sc)); for(int i=0;i<8;i++) { double a=i*Math.PI/4;g.DrawLine(p,x+S(8,sc)+(float)Math.Cos(a)*S(6,sc),y+S(8,sc)+(float)Math.Sin(a)*S(6,sc),x+S(8,sc)+(float)Math.Cos(a)*S(8,sc),y+S(8,sc)+(float)Math.Sin(a)*S(8,sc)); } }
            }
        }
    }
    internal class UiWindow : Form
    {
        internal const int CaptionHeight=42;
        internal readonly UiTitleBar TitleBar;
        internal string TitlePrimary, TitleSecondary;
        internal readonly float UiScale;
        internal int S(int px) { return UiTheme.S(px, UiScale); }
        internal int ScaledCaptionHeight { get { return S(CaptionHeight); } }

        internal bool CanResize = true;
        internal bool GlassEnabled { get; private set; }
        private bool localCorners;
        private UiCornerEdges cornerEdges;
        [StructLayout(LayoutKind.Sequential)] private struct PointNative { public int X,Y; }
        [StructLayout(LayoutKind.Sequential)] private struct MinMax { public PointNative Reserved,MaxSize,MaxPosition,MinTrack,MaxTrack; }
        [DllImport("user32.dll")] internal static extern bool ReleaseCapture();
        [DllImport("user32.dll")] internal static extern IntPtr SendMessage(IntPtr hwnd,int msg,IntPtr w,IntPtr l);
        internal UiWindow()
        {
            UiScale = UiTheme.SystemDpiScale();
            FormBorderStyle=FormBorderStyle.None;
            UiInput.Install(); SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw,true);
            AutoScaleMode=AutoScaleMode.None;
            Padding=new Padding(0,ScaledCaptionHeight,0,0);
            try { Icon=Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { Icon=SystemIcons.Application; }
            TitleBar=new UiTitleBar(this); TitleBar.Name="window-title"; Controls.Add(TitleBar);
            SizeChanged+=delegate { TitleBar.SetBounds(0,0,ClientSize.Width,ScaledCaptionHeight); TitleBar.BringToFront(); };
            TextChanged+=delegate { TitleBar.Invalidate(); };
        }
        protected override CreateParams CreateParams { get { CreateParams p=base.CreateParams; p.Style |= 0x00040000; return p; } }
        protected override void OnShown(EventArgs e) { TitleBar.SetBounds(0,0,ClientSize.Width,ScaledCaptionHeight); TitleBar.BringToFront(); base.OnShown(e); }
        internal void ApplyGlass(bool enabled)
        {
            GlassEnabled = enabled;
            if (IsHandleCreated) UiTheme.SetAcrylic(Handle, enabled);
        }
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            if (GlassEnabled) UiTheme.PaintSplitGlass(this, e.Graphics, ClientSize.Width, ClientSize.Height);
            else base.OnPaintBackground(e);
        }
        internal void UseLocalCorners(bool enabled)
        {
            if (!enabled && !localCorners) return;
            localCorners = enabled;
            UpdateCornerRegion();
        }
        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (localCorners) UpdateCornerRegion();
        }
        private void UpdateCornerRegion()
        {
            if (localCorners && WindowState == FormWindowState.Normal && Width > 0 && Height > 0)
            {
                if (cornerEdges == null) cornerEdges = new UiCornerEdges(this);
                Region = UiCornerEdges.Interior(Size);
                PlaceCornerEdges();
            }
            else
            {
                DestroyCornerEdges();
                if (Region != null) Region = null;
            }
        }
        protected override void OnLocationChanged(EventArgs e)
        {
            base.OnLocationChanged(e);
            PlaceCornerEdges();
        }
        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (localCorners && Visible) UpdateCornerRegion();
            else if (!Visible) DestroyCornerEdges();
        }
        protected override void OnHandleDestroyed(EventArgs e)
        {
            DestroyCornerEdges();
            base.OnHandleDestroyed(e);
        }
        private void DestroyCornerEdges()
        {
            if (cornerEdges == null) return;
            cornerEdges.Dispose();
            cornerEdges = null;
        }
        private void PlaceCornerEdges()
        {
            if (cornerEdges == null || !Visible || !IsHandleCreated || WindowState != FormWindowState.Normal) return;
            try { if (cornerEdges.Place()) return; }
            catch (System.ComponentModel.Win32Exception) { }
            DestroyCornerEdges();
            UiTheme.ClipRoundedWindow(this, UiTheme.WindowRadius);
        }
        protected override void WndProc(ref Message m)
        {
            if(m.Msg==0x83) { m.Result=IntPtr.Zero; return; }
            if(m.Msg==0x85) { m.Result=IntPtr.Zero; return; }
            if(m.Msg==0x86) { m.Result=new IntPtr(1); Invalidate(); return; }
            if(m.Msg==0x24)
            {
                base.WndProc(ref m); MinMax mm=(MinMax)Marshal.PtrToStructure(m.LParam,typeof(MinMax));
                Screen screen=Screen.FromHandle(Handle); Rectangle work=screen.WorkingArea;
                mm.MaxPosition.X=work.Left-screen.Bounds.Left; mm.MaxPosition.Y=work.Top-screen.Bounds.Top;
                mm.MaxSize.X=work.Width; mm.MaxSize.Y=work.Height; Marshal.StructureToPtr(mm,m.LParam,false); return;
            }
            base.WndProc(ref m);
            if(m.Msg==0x84 && WindowState==FormWindowState.Normal && CanResize)
            {
                int grip = S(6);
                long value=m.LParam.ToInt64(); Point point=PointToClient(new Point((short)(value & 0xffff),(short)((value>>16)&0xffff)));
                bool left=point.X<grip,right=point.X>=ClientSize.Width-grip,top=point.Y<grip,bottom=point.Y>=ClientSize.Height-grip;
                if(top) m.Result=new IntPtr(left?13:right?14:12); else if(bottom)m.Result=new IntPtr(left?16:right?17:15); else if(left)m.Result=new IntPtr(10); else if(right)m.Result=new IntPtr(11);
            }
        }
    }
    // The native form still owns its controls and the fully covered pixels. Small
    // click-through owned surfaces draw only the fractional pixels at each corner.
    internal sealed class UiCornerEdges : IDisposable
    {
        private const int Radius = UiTheme.WindowRadius;
        private static readonly byte[,] Coverage = BuildCoverage();
        private readonly UiWindow owner;
        private readonly NativeWindow[] windows = new NativeWindow[4];
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr window, int command);

        internal UiCornerEdges(UiWindow owner) { this.owner = owner; }

        private static byte[,] BuildCoverage()
        {
            const int samples = 8;
            var coverage = new byte[Radius, Radius];
            for (int y = 0; y < Radius; y++)
            for (int x = 0; x < Radius; x++)
            {
                int count = 0;
                for (int sy = 0; sy < samples; sy++)
                for (int sx = 0; sx < samples; sx++)
                {
                    double dx = Radius - x - (sx + .5) / samples, dy = Radius - y - (sy + .5) / samples;
                    if (dx * dx + dy * dy <= Radius * Radius) count++;
                }
                coverage[x, y] = (byte)Math.Round(255.0 * count / (samples * samples));
            }
            return coverage;
        }

        private static byte Alpha(int index, int x, int y)
        {
            return Coverage[(index & 1) == 0 ? x : Radius - 1 - x, index < 2 ? y : Radius - 1 - y];
        }

        private static Point LocalPosition(int index, Size size)
        {
            return new Point((index & 1) == 0 ? 0 : size.Width - Radius, index < 2 ? 0 : size.Height - Radius);
        }

        internal static Region Interior(Size size)
        {
            var region = new Region(new Rectangle(Point.Empty, size));
            for (int index = 0; index < 4; index++)
            {
                Point spot = LocalPosition(index, size);
                for (int y = 0; y < Radius; y++)
                for (int x = 0; x < Radius; x++)
                    if (Alpha(index, x, y) != 255) region.Exclude(new Rectangle(spot.X + x, spot.Y + y, 1, 1));
            }
            return region;
        }

        internal bool Place()
        {
            Rectangle bounds = owner.Bounds;
            for (int i = 0; i < windows.Length; i++)
            {
                bool create = windows[i] == null;
                if (create)
                {
                    windows[i] = new NativeWindow();
                    windows[i].CreateHandle(new CreateParams { Parent = owner.Handle, Style = unchecked((int)0x80000000),
                        ExStyle = 0x00080000 | 0x08000000 | 0x80 | 0x20 }); // Layered, no activate, tool window, click-through.
                }
                Point spot = LocalPosition(i, bounds.Size);
                spot.Offset(bounds.Location);
                Color background = (i & 1) == 0 && owner.Controls["sidebar"] != null ? UiTheme.Sidebar : owner.BackColor;
                using (var bitmap = new Bitmap(Radius, Radius, System.Drawing.Imaging.PixelFormat.Format32bppPArgb))
                {
                    for (int y = 0; y < Radius; y++)
                    for (int x = 0; x < Radius; x++)
                    {
                        byte alpha = Alpha(i, x, y);
                        if (alpha > 0 && alpha < 255) bitmap.SetPixel(x, y, Color.FromArgb(alpha, background));
                    }
                    if (!UiLayeredSurface.Present(windows[i].Handle, bitmap, spot, 255)) return false;
                }
                if (create) ShowWindow(windows[i].Handle, 4);
            }
            return true;
        }
        public void Dispose()
        {
            foreach (NativeWindow window in windows)
                if (window != null && window.Handle != IntPtr.Zero) window.DestroyHandle();
        }
    }
    internal sealed class UiTitleBar : Control
    {
        private readonly UiWindow window;
        private readonly UiCaption minimize,maximize,close;
        private Bitmap brandIcon;
        private int brandIconSize;
        private Font mainFont, subFont;
        private FontFamily cachedFamily;
        private bool cachedHasSecondary;
        internal UiTitleBar(UiWindow window)
        {
            this.window=window; TabStop=false; SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw,true);
            minimize=new UiCaption("minimize",delegate {window.WindowState=FormWindowState.Minimized;});
            maximize=new UiCaption("maximize",delegate {window.WindowState=window.WindowState==FormWindowState.Maximized?FormWindowState.Normal:FormWindowState.Maximized;});
            close=new UiCaption("close",delegate {window.Close();});
            Controls.Add(minimize);Controls.Add(maximize);Controls.Add(close);
            MouseDown+=delegate(object sender,MouseEventArgs e) { if(e.Button==MouseButtons.Left) {UiWindow.ReleaseCapture();UiWindow.SendMessage(window.Handle,0xA1,new IntPtr(2),IntPtr.Zero);} };
            DoubleClick+=delegate {if(window.MaximizeBox)window.WindowState=window.WindowState==FormWindowState.Maximized?FormWindowState.Normal:FormWindowState.Maximized;};
        }
        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e); if(close==null)return;
            int w = window.S(46);
            close.SetBounds(Width-w,0,w,Height);maximize.SetBounds(Width-w*2,0,w,Height);minimize.SetBounds(Width-w*3,0,w,Height);
            maximize.Visible=window.MaximizeBox; minimize.Visible=window.MinimizeBox;
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            UiTheme.PaintSplitGlass(window, e.Graphics, Width, Height);
            Control sidebar = window.Controls["sidebar"];
            if(window.Icon!=null)
            {
                int iconSize = window.S(18);
                if (brandIcon == null || brandIconSize != iconSize)
                {
                    if (brandIcon != null) brandIcon.Dispose();
                    brandIcon = TrayContext.RenderGauge(iconSize, 0, 0, true);
                    brandIconSize = iconSize;
                }
                e.Graphics.DrawImageUnscaled(brandIcon, window.S(15), (Height - iconSize) / 2);
            }
            string primary=String.IsNullOrEmpty(window.TitlePrimary)?window.Text:window.TitlePrimary;
            string secondary=window.TitleSecondary;
            bool hasSecondary = !String.IsNullOrEmpty(secondary);
            if (mainFont == null || !Equals(cachedFamily, window.Font.FontFamily) || cachedHasSecondary != hasSecondary)
            {
                if (mainFont != null) mainFont.Dispose();
                if (subFont != null) subFont.Dispose();
                cachedFamily = window.Font.FontFamily;
                cachedHasSecondary = hasSecondary;
                mainFont = new Font(cachedFamily, hasSecondary ? 9.3f : 10.5f, FontStyle.Bold);
                subFont = new Font(cachedFamily, 8.0f, FontStyle.Regular);
            }
            const TextFormatFlags measureFlags = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;
            const TextFormatFlags drawFlags = measureFlags | TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter;
            Size unconstrained = new Size(Int32.MaxValue, Int32.MaxValue);
            Size mainSize = TextRenderer.MeasureText(e.Graphics, primary, mainFont, unconstrained, measureFlags);
            Size subSize = hasSecondary ? TextRenderer.MeasureText(e.Graphics, secondary, subFont, unconstrained, measureFlags) : Size.Empty;
            Rectangle mainBounds, subBounds;
            int captionStart = minimize.Visible ? minimize.Left : maximize.Visible ? maximize.Left : close.Left;
            PlaceTitle(mainSize, subSize, Height, Math.Min(captionStart, sidebar == null ? Width : sidebar.Right) - window.S(12), out mainBounds, out subBounds, window.UiScale);
            UiTheme.DrawText(this, e.Graphics, primary, mainFont, mainBounds, UiTheme.TextColor, drawFlags);
            if (!subBounds.IsEmpty)
                UiTheme.DrawText(this, e.Graphics, secondary, subFont, subBounds, UiTheme.IsGlass(this) ? UiTheme.GlassMuted : UiTheme.Muted, drawFlags);
        }

        internal static void PlaceTitle(Size primary, Size secondary, int height, int right, out Rectangle main, out Rectangle sub, float scale = 1f)
        {
            int left = UiTheme.S(42, scale), gap = UiTheme.S(8, scale);
            int width = Math.Max(1, right - left);
            sub = Rectangle.Empty;
            if (secondary.IsEmpty || primary.Width + gap + secondary.Width <= width)
            {
                main = new Rectangle(left, 0, Math.Min(primary.Width + 2, width), height);
                if (!secondary.IsEmpty) sub = new Rectangle(left + primary.Width + gap, 0, width - primary.Width - gap, height);
                return;
            }
            int padding = UiTheme.S(5, scale);
            int top = Math.Max(padding, (height - primary.Height - secondary.Height) / 2);
            int bottom = Math.Max(top, height - padding);
            main = new Rectangle(left, top, width, Math.Min(primary.Height, Math.Max(0, bottom - top - secondary.Height)));
            sub = new Rectangle(left, main.Bottom, width, Math.Min(secondary.Height, bottom - main.Bottom));
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (brandIcon != null) { brandIcon.Dispose(); brandIcon = null; }
                if (mainFont != null) { mainFont.Dispose(); mainFont = null; }
                if (subFont != null) { subFont.Dispose(); subFont = null; }
                cachedFamily = null;
            }
            base.Dispose(disposing);
        }
    }
    internal sealed class UiCaption : Control
    {
        private readonly string kind; private readonly Action action; private bool hover;
        internal UiCaption(string kind,Action action)
        {
            this.kind=kind;this.action=action;Name="caption-"+kind;AccessibleName=kind;AccessibleRole=AccessibleRole.PushButton;TabStop=false;
            SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);
        }
        protected override void OnMouseEnter(EventArgs e){hover=true;Invalidate();base.OnMouseEnter(e);}
        protected override void OnMouseLeave(EventArgs e){hover=false;Invalidate();base.OnMouseLeave(e);}
        protected override void OnClick(EventArgs e){action();base.OnClick(e);}
        protected override void OnPaint(PaintEventArgs e)
        {
            UiTheme.PaintUnderlay(this, e.Graphics, ClientRectangle, UiTheme.Background);
            bool isClose = kind == "close";
            float s = UiTheme.DpiScaleOf(this);
            // Hover mask: an inset rounded rectangle that nests inside the window's
            // own corner instead of the old full-height square.
            int inset = UiTheme.S(3, s);
            RectangleF mask = new RectangleF(inset, inset, Math.Max(1, Width - inset * 2), Math.Max(1, Height - inset * 2));
            float radius = UiTheme.Radius;
            float topRight = isClose ? Math.Max(2f, UiTheme.WindowRadius - inset) : radius;
            if (hover)
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                Color maskFill = isClose
                    ? Color.FromArgb(186, 46, 62)
                    : (UiTheme.IsGlass(this) && !UiTheme.Dark ? Color.FromArgb(34, 15, 23, 42) : UiTheme.Input);
                using (GraphicsPath path = UiTheme.RoundCorners(mask, radius, topRight, radius, radius))
                using (Brush fill = new SolidBrush(maskFill))
                    e.Graphics.FillPath(fill, path);
            }
            int x = Width / 2, y = Height / 2;
            int minArm = UiTheme.S(5, s), arm = UiTheme.S(4, s);
            using (Pen p = new Pen(hover && isClose ? Color.White : UiTheme.TextColor, 1))
            {
                if (kind == "minimize") e.Graphics.DrawLine(p, x - minArm, y, x + minArm, y);
                else if (kind == "maximize") e.Graphics.DrawRectangle(p, x - arm, y - arm, arm * 2, arm * 2);
                else { e.Graphics.DrawLine(p, x - arm, y - arm, x + arm, y + arm); e.Graphics.DrawLine(p, x + arm, y - arm, x - arm, y + arm); }
            }
        }
    }
    internal sealed class UiComboBox : Control
    {
        internal readonly List<object> Items=new List<object>();
        internal event EventHandler SelectedIndexChanged;
        private int selected=-1; private bool hover; private UiPopup popup;
        internal int SelectedIndex
        {
            get{return selected;}
            set {if(value<-1 || value>=Items.Count)throw new ArgumentOutOfRangeException("value");if(value==selected)return;selected=value;Text=value<0?"":Convert.ToString(Items[value]);AccessibleName=Text;Invalidate();if(SelectedIndexChanged!=null)SelectedIndexChanged(this,EventArgs.Empty);}
        }
        internal UiComboBox()
        {
            float s=UiTheme.SystemDpiScale();
            SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.Selectable|ControlStyles.ResizeRedraw,true);
            Size=new Size(UiTheme.S(184,s),UiTheme.S(33,s));Margin=new Padding(0,0,0,UiTheme.S(10,s));TabStop=true;AccessibleRole=AccessibleRole.ComboBox;Cursor=Cursors.Hand;
        }
        internal bool PopupOpen { get { return popup != null && !popup.IsDisposed; } }
        protected override bool IsInputKey(Keys keyData)
        {
            Keys key = keyData & Keys.KeyCode;
            return key == Keys.Up || key == Keys.Down || key == Keys.Home || key == Keys.End ||
                (PopupOpen && (key == Keys.Escape || key == Keys.Enter)) || base.IsInputKey(keyData);
        }
        protected override void OnMouseEnter(EventArgs e){hover=true;Invalidate();base.OnMouseEnter(e);}
        protected override void OnMouseLeave(EventArgs e){hover=false;Invalidate();base.OnMouseLeave(e);}
        protected override void OnMouseDown(MouseEventArgs e){base.OnMouseDown(e);if(e.Button==MouseButtons.Left){UiInput.Keyboard=false;Focus();Open();}}
        protected override void OnGotFocus(EventArgs e){Invalidate();base.OnGotFocus(e);}
        protected override void OnLostFocus(EventArgs e)
        {
            if (PopupOpen) popup.Close();
            Invalidate();
            base.OnLostFocus(e);
        }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            UiInput.Keyboard=true;
            bool toggle = e.KeyCode == Keys.F4 || (e.Alt && e.KeyCode == Keys.Down);
            if (PopupOpen && !toggle && popup.HandleNavigation(e.KeyCode)) e.Handled = true;
            else if(e.KeyCode==Keys.Space||e.KeyCode==Keys.Enter||toggle){Open();e.Handled=true;}
            else if(Items.Count>0)
            {
                if(e.KeyCode==Keys.Down){SelectedIndex=Math.Min(Items.Count-1,selected+1);e.Handled=true;}
                else if(e.KeyCode==Keys.Up){SelectedIndex=Math.Max(0,selected-1);e.Handled=true;}
                else if(e.KeyCode==Keys.Home){SelectedIndex=0;e.Handled=true;}
                else if(e.KeyCode==Keys.End){SelectedIndex=Items.Count-1;e.Handled=true;}
            }
            base.OnKeyDown(e);
        }
        internal void Open()
        {
            if(!Enabled || Items.Count==0)return;
            if(PopupOpen){popup.Close();return;}
            string[] labels=Items.ConvertAll<string>(delegate(object x){return Convert.ToString(x);}).ToArray();
            popup=new UiPopup(labels,selected,false,Font,delegate(int i){SelectedIndex=i;});
            popup.Open(this, null, 0, false);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            UiTheme.PaintUnderlay(this, e.Graphics, ClientRectangle, Parent==null?UiTheme.Surface:Parent.BackColor);
            bool lightGlass = UiTheme.IsGlass(this) && !UiTheme.Dark;
            if (lightGlass)
                UiTheme.LightGlassMaterial(e.Graphics, ClientRectangle, false, false, hover, false, Enabled);
            else
                UiTheme.Material(e.Graphics,ClientRectangle,BackColor,false,false,hover,false,Enabled);
            float s = UiTheme.DpiScaleOf(this);
            UiTheme.DrawText(this,e.Graphics,Text,Font,new Rectangle(UiTheme.S(12,s),0,Width-UiTheme.S(42,s),Height),Enabled?UiTheme.TextColor:UiTheme.Muted,TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine|TextFormatFlags.EndEllipsis);
            e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
            Color arrowColor = !Enabled ? UiTheme.Muted : (lightGlass ? UiTheme.TextColor : UiTheme.Muted);
            int ax = Width - UiTheme.S(18, s), ay = Height / 2, aw = UiTheme.S(5, s), ah = UiTheme.S(2, s), ab = UiTheme.S(3, s);
            using(Pen p=new Pen(arrowColor, lightGlass ? 1.4f : 1.2f))e.Graphics.DrawLines(p,new Point[] {new Point(ax-aw,ay-ah),new Point(ax,ay+ab),new Point(ax+aw,ay-ah)});
            if(Focused && UiInput.Keyboard)UiTheme.Focus(e.Graphics,ClientRectangle);
        }
        protected override void Dispose(bool disposing){if(disposing&&popup!=null)popup.Dispose();base.Dispose(disposing);}
    }
}


namespace QuotaTrayApp
{
    // The popup owns its DPI context and renders its layered bitmap at the monitor's actual pixel size.
    internal static class UiLayeredSurface
    {
        [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] private struct NativeSize { public int Width, Height; }
        [StructLayout(LayoutKind.Sequential)]
        private struct BitmapHeader
        {
            public uint Size;
            public int Width, Height;
            public ushort Planes, BitCount;
            public uint Compression, ImageSize;
            public int HorizontalResolution, VerticalResolution;
            public uint PaletteColors, ImportantColors;
        }
        [StructLayout(LayoutKind.Sequential, Pack = 1)] private struct Blend { public byte Op, Flags, Alpha, Format; }
        [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateDIBSection(IntPtr context, ref BitmapHeader header,
            uint usage, out IntPtr pixels, IntPtr section, uint offset);
        [DllImport("user32.dll")] private static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr dst,
            ref NativePoint point, ref NativeSize size, IntPtr src, ref NativePoint origin, int key, ref Blend blend, int flags);

        internal static bool Present(IntPtr window, Bitmap bitmap, Point position, byte opacity)
        {
            IntPtr screen = GetDC(IntPtr.Zero), dc = IntPtr.Zero, surface = IntPtr.Zero, previous = IntPtr.Zero;
            try
            {
                dc = CreateCompatibleDC(screen);
                if (dc == IntPtr.Zero) return false;
                var header = new BitmapHeader
                {
                    Size = (uint)Marshal.SizeOf(typeof(BitmapHeader)), Width = bitmap.Width, Height = -bitmap.Height,
                    Planes = 1, BitCount = 32
                };
                IntPtr pixels;
                surface = CreateDIBSection(screen, ref header, 0, out pixels, IntPtr.Zero, 0);
                if (surface == IntPtr.Zero || pixels == IntPtr.Zero) return false;
                // Preserve alpha byte for byte. GetHbitmap composites fully transparent
                // GDI+ pixels against a background color on some Windows versions.
                var bounds = new Rectangle(Point.Empty, bitmap.Size);
                var data = bitmap.LockBits(bounds, System.Drawing.Imaging.ImageLockMode.ReadOnly,
                    System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
                try
                {
                    int rowBytes = bitmap.Width * 4;
                    byte[] bytes = new byte[rowBytes];
                    for (int y = 0; y < bitmap.Height; y++)
                    {
                        Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), bytes, 0, rowBytes);
                        Marshal.Copy(bytes, 0, IntPtr.Add(pixels, y * rowBytes), rowBytes);
                    }
                }
                finally { bitmap.UnlockBits(data); }
                previous = SelectObject(dc, surface);
                if (previous == IntPtr.Zero || previous == new IntPtr(-1)) return false;
                var point = new NativePoint { X = position.X, Y = position.Y };
                var size = new NativeSize { Width = bitmap.Width, Height = bitmap.Height };
                var origin = new NativePoint();
                var blend = new Blend { Alpha = opacity, Format = 1 };
                return UpdateLayeredWindow(window, screen, ref point, ref size, dc, ref origin, 0, ref blend, 2);
            }
            finally
            {
                if (previous != IntPtr.Zero && previous != new IntPtr(-1)) SelectObject(dc, previous);
                if (surface != IntPtr.Zero) DeleteObject(surface);
                if (dc != IntPtr.Zero) DeleteDC(dc);
                if (screen != IntPtr.Zero) ReleaseDC(IntPtr.Zero, screen);
            }
        }
    }

    // 亚克力底：抓取窗口背后的区域，模糊后作为半透明底，和提醒弹窗同一套观感。
    internal static class UiBackdrop
    {
        // The approved tint stays fixed. Dark mode softens only the brightest backdrop values.
        internal static int TintAlpha = 102;
        internal static int LightTintAlpha = 138;
        private const int PopupPadding = 72, PopupDownscale = 12, PopupSigma = 22;
        private static readonly Color DarkTint = Color.FromArgb(21, 23, 27);
        private static readonly double[] GaussianWeights = CreateGaussianWeights();
        private static readonly double[] HighlightScale = CreateHighlightScale();
        internal static Color PopupTextColor { get { return UiTheme.Dark ? Color.FromArgb(249, 250, 252) : UiTheme.TextColor; } }
        internal static bool Allowed()
        {
            if (SystemInformation.HighContrast || SystemInformation.TerminalServerSession) return false;
            try
            {
                const string key = @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
                return !Object.Equals(Microsoft.Win32.Registry.GetValue(key, "EnableTransparency", 1), 0);
            }
            catch (System.Security.SecurityException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }
        internal static Bitmap Capture(Rectangle screenArea, bool tray)
        {
            if (!Allowed() || screenArea.Width < 2 || screenArea.Height < 2) return null;
            Rectangle area = screenArea;
            int padding = tray ? PopupPadding : 14;
            area.Inflate(padding, padding);
            if (tray) area.Intersect(Screen.FromRectangle(screenArea).Bounds);
            if (!area.Contains(screenArea)) return null;
            try
            {
                using (var sample = new Bitmap(area.Width, area.Height, System.Drawing.Imaging.PixelFormat.Format24bppRgb))
                {
                    using (Graphics capture = Graphics.FromImage(sample))
                        capture.CopyFromScreen(area.Location, Point.Empty, area.Size);
                    return FilterBackdrop(sample, new Rectangle(screenArea.X - area.X, screenArea.Y - area.Y, screenArea.Width, screenArea.Height), tray, UiTheme.Dark);
                }
            }
            catch (System.ComponentModel.Win32Exception) { return null; }
        }

        internal static Bitmap FilterBackdrop(Bitmap sample, Rectangle crop, bool tray, bool dark)
        {
            int step = tray ? PopupDownscale : 8;
            using (var reduced = new Bitmap(Math.Max(1, sample.Width / step), Math.Max(1, sample.Height / step), PixelFormat.Format24bppRgb))
            using (var edges = new ImageAttributes())
            {
                edges.SetWrapMode(WrapMode.TileFlipXY);
                using (Graphics down = Graphics.FromImage(reduced))
                {
                    down.InterpolationMode = InterpolationMode.HighQualityBilinear;
                    down.DrawImage(sample, new Rectangle(Point.Empty, reduced.Size), 0, 0, sample.Width, sample.Height, GraphicsUnit.Pixel, edges);
                }
                if (tray) BlurReducedBitmap(reduced, dark);
                var blurred = new Bitmap(crop.Width, crop.Height, PixelFormat.Format24bppRgb);
                try
                {
                    using (Graphics up = Graphics.FromImage(blurred))
                    {
                        up.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        up.DrawImage(reduced, new Rectangle(-crop.X, -crop.Y, sample.Width, sample.Height), 0, 0, reduced.Width, reduced.Height, GraphicsUnit.Pixel, edges);
                    }
                    return blurred;
                }
                catch { blurred.Dispose(); throw; }
            }
        }

        private static double[] CreateGaussianWeights()
        {
            double sigma = (double)PopupSigma / PopupDownscale;
            int radius = (int)Math.Ceiling(3 * sigma);
            var weights = new double[2 * radius + 1];
            double total = 0;
            for (int i = -radius; i <= radius; i++)
                total += weights[i + radius] = Math.Exp(-i * i / (2 * sigma * sigma));
            for (int i = 0; i < weights.Length; i++) weights[i] /= total;
            return weights;
        }

        private static double[] CreateHighlightScale()
        {
            const int knee = 114, shoulder = 65;
            var scale = new double[256];
            for (int luma = 0; luma < scale.Length; luma++)
                scale[luma] = luma <= knee ? 1 : (knee + shoulder * (1 - Math.Exp(-(luma - knee) / (double)shoulder))) / luma;
            return scale;
        }

        private static void BlurReducedBitmap(Bitmap bitmap, bool softenHighlights)
        {
            BitmapData data = bitmap.LockBits(new Rectangle(Point.Empty, bitmap.Size), ImageLockMode.ReadWrite, PixelFormat.Format24bppRgb);
            try
            {
                int stride = data.Stride;
                var pixels = new byte[stride * bitmap.Height];
                var temporary = new byte[pixels.Length];
                Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
                BlurPass(pixels, temporary, bitmap.Width, bitmap.Height, stride, true);
                BlurPass(temporary, pixels, bitmap.Width, bitmap.Height, stride, false);
                if (softenHighlights)
                {
                    for (int y = 0; y < bitmap.Height; y++)
                    for (int x = 0; x < bitmap.Width; x++)
                    {
                        int index = y * stride + x * 3;
                        int luma = (21 * pixels[index + 2] + 72 * pixels[index + 1] + 7 * pixels[index] + 50) / 100;
                        double scale = HighlightScale[luma];
                        for (int channel = 0; channel < 3; channel++)
                            pixels[index + channel] = (byte)Math.Round(pixels[index + channel] * scale);
                    }
                }
                Marshal.Copy(pixels, 0, data.Scan0, pixels.Length);
            }
            finally { bitmap.UnlockBits(data); }
        }

        private static void BlurPass(byte[] source, byte[] target, int width, int height, int stride, bool horizontal)
        {
            int radius = GaussianWeights.Length / 2;
            int size = horizontal ? width : height;
            var taps = new int[size, GaussianWeights.Length];
            for (int position = 0; position < size; position++)
            for (int i = 0; i < GaussianWeights.Length; i++)
            {
                int reflected = position + i - radius;
                while (reflected < 0 || reflected >= size)
                    reflected = reflected < 0 ? -reflected - 1 : 2 * size - reflected - 1;
                taps[position, i] = reflected * (horizontal ? 3 : stride);
            }
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            for (int channel = 0; channel < 3; channel++)
            {
                int origin = horizontal ? y * stride : x * 3;
                int position = horizontal ? x : y;
                double sum = 0;
                for (int i = 0; i < GaussianWeights.Length; i++)
                    sum += source[origin + taps[position, i] + channel] * GaussianWeights[i];
                target[y * stride + x * 3 + channel] = (byte)Math.Round(sum);
            }
        }
        internal static void Paint(Graphics graphics, RectangleF body, Bitmap backdrop, float radius, Color? tint = null)
        {
            if (backdrop == null) return;
            GraphicsState saved = graphics.Save();
            using (GraphicsPath shape = UiTheme.Round(body, radius))
            {
                graphics.SetClip(shape, CombineMode.Intersect);
                PaintOverlay(graphics, body, backdrop, tint);
            }
            graphics.Restore(saved);
        }

        internal static void PaintOverlay(Graphics graphics, RectangleF body, Bitmap backdrop, Color? tint = null)
        {
            Color color = tint ?? (UiTheme.Dark ? Color.FromArgb(TintAlpha, DarkTint) : Color.FromArgb(LightTintAlpha, 246, 249, 252));
            graphics.DrawImageUnscaled(backdrop, (int)Math.Round(body.X), (int)Math.Round(body.Y));
            using (var brush = new SolidBrush(color)) graphics.FillRectangle(brush, body);
        }

        internal static void DrawGlassEdge(Graphics graphics, RectangleF body, float radius)
        {
            using (GraphicsPath outline = UiTheme.Round(body, radius))
            using (var reflection = new LinearGradientBrush(RectangleF.Inflate(body, 1, 1), Color.FromArgb(164, Color.White),
                Color.FromArgb(84, Color.Black), LinearGradientMode.Vertical))
            using (var edge = new Pen(reflection)) graphics.DrawPath(edge, outline);
        }

        internal static void PaintPopup(Graphics graphics, RectangleF body, Bitmap backdrop, int radius, Func<int, int> scale, int lightShadow, bool tray)
        {
            bool sharedDark = tray && UiTheme.Dark;
            for (int i = sharedDark ? 7 : lightShadow; i >= 1; i--)
            {
                RectangleF shadow = body;
                shadow.Inflate(scale(i), scale(i));
                shadow.Offset(0, scale(2));
                using (GraphicsPath path = UiTheme.Round(shadow, radius + i * .6f))
                using (var brush = new SolidBrush(Color.FromArgb(3 + (7 - i), 0, 0, 0)))
                    graphics.FillPath(brush, path);
            }
            if (backdrop == null) { UiTheme.Fill(graphics, Rectangle.Round(body), UiTheme.Surface, UiTheme.Border); return; }
            Color? tint = !tray && UiTheme.Dark ? Color.FromArgb(77, 29, 33, 42) : (Color?)null;
            Paint(graphics, body, backdrop, radius, tint);
            if (tray) DrawGlassEdge(graphics, RectangleF.Inflate(body, -1, -1), radius - 1);
            else
            {
                Color color = UiTheme.Dark ? UiTheme.Border : Color.FromArgb(185, 168, 178, 190);
                using (var edge = new Pen(color))
                using (GraphicsPath outline = UiTheme.Round(body, radius)) graphics.DrawPath(edge, outline);
            }
        }
    }

    // Capture the first backdrop before showing the window. Later frames exclude the window itself.
    internal sealed class UiLiveBackdrop : IDisposable
    {
        private readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 40 };
        private readonly Func<Rectangle> area;
        private readonly Action repaint;
        private readonly bool tray;
        internal Bitmap Frame { get; private set; }

        [DllImport("user32.dll")] private static extern bool SetWindowDisplayAffinity(IntPtr window, uint affinity);

        internal UiLiveBackdrop(Func<Rectangle> area, Action repaint, bool tray = true)
        {
            this.area = area;
            this.repaint = repaint;
            this.tray = tray;
            timer.Tick += delegate
            {
                Rectangle bounds = this.area();
                if (bounds.IsEmpty || !Screen.FromRectangle(bounds).Bounds.Contains(bounds)) return;
                Bitmap next = UiBackdrop.Capture(bounds, this.tray);
                if (next == null) return;
                Replace(next);
                this.repaint();
            };
        }

        internal void CaptureInitial(Rectangle bounds) { Replace(UiBackdrop.Capture(bounds, tray)); }

        internal void Start(Form window)
        {
            if (!UiBackdrop.Allowed()) return;
            try { if (!SetWindowDisplayAffinity(window.Handle, 0x11)) return; }
            catch (EntryPointNotFoundException) { return; }
            timer.Start();
        }

        internal void Stop() { timer.Stop(); }

        private void Replace(Bitmap next)
        {
            if (Frame != null) Frame.Dispose();
            Frame = next;
        }

        public void Dispose()
        {
            timer.Dispose();
            Replace(null);
        }
    }

    internal sealed class UiPopup : Form, IMessageFilter
    {
        internal const int Shadow=9, RowHeight=32;
        private readonly string[] labels; private readonly bool menu; private readonly Action<int> choose;
        private readonly UiLiveBackdrop backdrop;
        private readonly int dangerIndex;
        private readonly string status;
        private readonly System.Windows.Forms.Timer animation=new System.Windows.Forms.Timer();
        private int active; private DateTime started; private Rectangle destination; private bool ready;
        private float progress;
        private int dpi = 96;
        private int shadow = Shadow, rowHeight = RowHeight;
        private IntPtr dpiContext;
        private Form comboOwner;
        private IntPtr comboAnchor;
        private const int PerMonitorAware = -3;
        [DllImport("user32.dll")] private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
        [DllImport("user32.dll")] private static extern IntPtr GetWindowDpiAwarenessContext(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out WindowRect bounds);
        [StructLayout(LayoutKind.Sequential)] private struct WindowRect { public int Left, Top, Right, Bottom; }
        internal int AnimationFrames;
        [DllImport("user32.dll")] private static extern bool SystemParametersInfo(int action,int param,out bool value,int flags);
        internal UiPopup(string[] labels,int selected,bool menu,Font font,Action<int> choose,int dangerIndex=-1,string status=null)
        {
            this.labels=labels;this.active=menu ? -1 : Math.Max(0,selected);this.menu=menu;this.choose=choose;
            this.dangerIndex=dangerIndex;
            this.status=status;
            backdrop = new UiLiveBackdrop(BackdropBounds, delegate { RenderSurface(false); }, menu);
            Font=font; FormBorderStyle=FormBorderStyle.None; ShowInTaskbar=false; TopMost=menu; StartPosition=FormStartPosition.Manual;AutoScaleMode=AutoScaleMode.None;
            AccessibleRole=menu?AccessibleRole.MenuPopup:AccessibleRole.List;Text=menu?L.T("设置"):"";
            AccessibleDescription = status;
            animation.Interval=15;animation.Tick+=delegate {progress=Math.Min(1,(float)(DateTime.UtcNow-started).TotalMilliseconds/160);RenderSurface();if(progress>=1)animation.Stop();};
        }
        // A settings dropdown uses its owner's focus and Z-order. The tray menu takes focus itself.
        protected override bool ShowWithoutActivation { get { return !menu; } }
        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams p = base.CreateParams;
                p.ExStyle |= 0x80000 | 0x80;
                if (!menu) p.ExStyle |= 0x08000000; // WS_EX_NOACTIVATE
                return p;
            }
        }
        internal static Rectangle Placement(Size size,Rectangle anchor,Rectangle work,bool tray,int shadow=Shadow,int gap=6)
        {
            int x=anchor.Left-shadow,y=anchor.Bottom+gap-shadow;
            if(tray)
            {
                if(anchor.Left>=work.Right)x=work.Right-size.Width-gap;
                else if(anchor.Right<=work.Left)x=work.Left+gap;
                else x=anchor.Right-size.Width+shadow;
                if(anchor.Top>=work.Bottom)y=work.Bottom-size.Height-gap;
                else if(anchor.Bottom<=work.Top)y=work.Top+gap;
                else y=anchor.Top-size.Height-gap;
            }
            else if(y+size.Height>work.Bottom)y=anchor.Top-size.Height-gap+shadow;
            x=Math.Max(work.Left,Math.Min(x,work.Right-size.Width));y=Math.Max(work.Top,Math.Min(y,work.Bottom-size.Height));
            return new Rectangle(x,y,size.Width,size.Height);
        }
        private int ScalePixel(int pixels) { return (int)Math.Round(pixels * dpi / 96.0); }
        private int StatusHeight { get { return String.IsNullOrEmpty(status) ? 0 : ScalePixel(28); } }
        private int RowTop { get { return shadow + ScalePixel(6) + StatusHeight; } }

        internal Rectangle Open(Control owner, Func<Rectangle> getAnchor, int bodyWidth, bool tray)
        {
            IntPtr previous = IntPtr.Zero;
            try { previous = SetThreadDpiAwarenessContext(new IntPtr(PerMonitorAware)); }
            catch (EntryPointNotFoundException) { }
            try
            {
                Rectangle anchor;
                WindowRect bounds;
                if (owner == null) anchor = getAnchor();
                else if (GetWindowRect(owner.Handle, out bounds))
                    anchor = Rectangle.FromLTRB(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom);
                else anchor = owner.RectangleToScreen(owner.ClientRectangle);
                Location = anchor.Location;
                if (previous != IntPtr.Zero)
                {
                    dpiContext = GetWindowDpiAwarenessContext(Handle);
                    uint monitorDpi = GetDpiForWindow(Handle);
                    if (monitorDpi > 0) dpi = (int)monitorDpi;
                }
                shadow = ScalePixel(Shadow);
                rowHeight = ScalePixel(RowHeight);
                int width = Math.Max(ScalePixel(144), tray ? ScalePixel(bodyWidth) : anchor.Width);
                Size = new Size(width + 2 * shadow, labels.Length * rowHeight + ScalePixel(12) + 2 * shadow + StatusHeight);
                Rectangle work = Screen.FromRectangle(anchor).WorkingArea;
                destination = Placement(Size, anchor, work, tray, shadow, ScalePixel(6));
                Bounds = destination;
                backdrop.CaptureInitial(BackdropBounds());
                bool animate = true;
                SystemParametersInfo(0x1002, 0, out animate, 0);
                progress = animate ? .01f : 1;
                started = DateTime.UtcNow;
                Form parent = owner == null ? null : owner.FindForm();
                if (parent == null) Show();
                else Show(parent);
                if (!menu && parent != null)
                {
                    comboOwner = parent;
                    comboAnchor = owner.Handle;
                    comboOwner.Deactivate += OnOwnerDeactivate;
                    Application.AddMessageFilter(this);
                }
                ready = true;
                RenderSurface();
                if (menu) backdrop.Start(this);
                if (menu) Activate();
                if (animate) animation.Start();
                return anchor;
            }
            finally
            {
                if (previous != IntPtr.Zero) SetThreadDpiAwarenessContext(previous);
            }
        }

        private Rectangle RowBounds(int index)
        {
            int inset = ScalePixel(5);
            return new Rectangle(shadow + inset, RowTop + index * rowHeight, Width - 2 * (shadow + inset), rowHeight);
        }

        private Rectangle BackdropBounds() { return Rectangle.Inflate(destination, -shadow, -shadow); }

        internal Bitmap RenderBitmap()
        {
            Bitmap bitmap=new Bitmap(Width,Height,System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            using(Graphics g=Graphics.FromImage(bitmap))
            {
                g.Clear(Color.Transparent);g.SmoothingMode=SmoothingMode.AntiAlias;g.PixelOffsetMode=PixelOffsetMode.HighQuality;
                Rectangle body = new Rectangle(shadow, shadow, Width - shadow * 2, Height - shadow * 2);
                int radius = ScalePixel(menu && UiTheme.Dark ? UiTheme.WindowRadius : UiTheme.Radius);
                UiBackdrop.PaintPopup(g, body, backdrop.Frame, radius, ScalePixel, 7, menu);
                int textInset = ScalePixel(menu ? 36 : 12), trailing = ScalePixel(menu ? 10 : 12);
                Color dangerInk = UiTheme.Dark ? Color.FromArgb(252, 154, 158) : Color.FromArgb(161, 35, 52);
                Color mutedInk = UiTheme.Dark ? UiTheme.Muted : Color.FromArgb(66, 76, 88);
                using (Font textFont = new Font(Font.FontFamily, Font.SizeInPoints * dpi / 72f, FontStyle.Regular, GraphicsUnit.Pixel))
                using (var foreground = new SolidBrush(menu ? UiBackdrop.PopupTextColor : UiTheme.TextColor))
                using (var danger = new SolidBrush(menu && UiTheme.Dark ? UiTheme.QuotaInk(0) : dangerInk))
                using (var format = new StringFormat(StringFormat.GenericDefault))
                {
                    g.PixelOffsetMode = PixelOffsetMode.Default;
                    g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                    format.LineAlignment = StringAlignment.Center;
                    format.Trimming = StringTrimming.EllipsisCharacter;
                    format.FormatFlags |= StringFormatFlags.NoWrap;
                    if (StatusHeight > 0)
                        using (var muted = new SolidBrush(menu && UiTheme.Dark ? UiBackdrop.PopupTextColor : mutedInk))
                            g.DrawString(status, textFont, muted, new Rectangle(body.Left + ScalePixel(12),
                                body.Top + ScalePixel(4), body.Width - ScalePixel(24), StatusHeight), format);
                    for (int i = 0; i < labels.Length; i++)
                    {
                        Rectangle row = RowBounds(i);
                        bool isDanger = i == dangerIndex;
                        if (i == active)
                        {
                            Color hoverFill = isDanger
                                ? Color.FromArgb(UiTheme.Dark ? 190 : 170, UiTheme.Blend(UiTheme.Surface, dangerInk, UiTheme.Dark ? 40 : 24))
                                : (UiTheme.Dark ? Color.FromArgb(200, UiTheme.Input) : Color.FromArgb(26, 15, 23, 42));
                            using (GraphicsPath hp = UiTheme.Round(row, ScalePixel(4)))
                            using (Brush hb = new SolidBrush(hoverFill))
                                g.FillPath(hb, hp);
                        }
                        var iconBounds = new Rectangle(row.Left + ScalePixel(10), row.Top + ScalePixel(8), ScalePixel(16), ScalePixel(16));
                        if (isDanger) UiSymbols.Action(g, "exit", iconBounds, dangerInk);
                        else if (menu) UiSymbols.Navigation(g, i == 0 ? "refresh" : "general", iconBounds, mutedInk);

                        // Rasterize glyphs at the final position. GDI's memory-DC text can lose
                        // its alpha/metrics when copied into the layered popup on Windows 10.
                        var textArea = new Rectangle(row.Left + textInset, row.Top,
                            Math.Max(1, row.Width - textInset - trailing), row.Height);
                        g.DrawString(labels[i], textFont, isDanger ? danger : foreground, textArea, format);
                    }
                }
            }
            return bitmap;
        }
        private void RenderSurface(bool countAnimation = true)
        {
            if(!ready || IsDisposed)return;
            if (countAnimation) AnimationFrames++;
            IntPtr previous = dpiContext == IntPtr.Zero ? IntPtr.Zero : SetThreadDpiAwarenessContext(dpiContext);
            try { PaintLayeredWindow(); }
            finally { if (previous != IntPtr.Zero) SetThreadDpiAwarenessContext(previous); }
        }

        private void PaintLayeredWindow()
        {
            float eased = 1 - (float)Math.Pow(1 - progress, 3);
            Point position = new Point(destination.X, destination.Y + (int)Math.Round((1 - eased) * ScalePixel(5)));
            using (Bitmap bitmap = RenderBitmap())
                UiLayeredSurface.Present(Handle, bitmap, position, (byte)Math.Round(255 * eased));
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            int index=(e.Y-RowTop)/rowHeight;
            if(e.Y>=RowTop&&index>=0&&index<labels.Length&&index!=active){active=index;RenderSurface();}
            base.OnMouseMove(e);
        }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            int rowTop = RowTop;
            if (e.Button == MouseButtons.Left && e.X >= shadow && e.X < Width - shadow &&
                e.Y >= rowTop && e.Y < rowTop + labels.Length * rowHeight)
            {
                active = (e.Y - rowTop) / rowHeight;
                Commit();
            }
            base.OnMouseUp(e);
        }
        private void Commit(){if(active<0)return;int index=active;Close();choose(index);}
        protected override bool IsInputKey(Keys keyData){return true;}
        internal bool HandleNavigation(Keys key)
        {
            switch (key)
            {
                case Keys.Escape: Close(); return true;
                case Keys.Enter:
                case Keys.Space: Commit(); return true;
                case Keys.Up: active = active < 0 ? labels.Length - 1 : (active + labels.Length - 1) % labels.Length; break;
                case Keys.Down: active = (active + 1) % labels.Length; break;
                case Keys.Home: active = 0; break;
                case Keys.End: active = labels.Length - 1; break;
                default: return false;
            }
            RenderSurface();
            return true;
        }
        protected override void OnKeyDown(KeyEventArgs e) { HandleNavigation(e.KeyCode); e.Handled = true; }
        protected override void OnDeactivate(EventArgs e) { base.OnDeactivate(e); if (menu && ready) Close(); }
        // 设置下拉必须保持所属窗口的激活状态。点它时 Windows 会先发 WM_MOUSEACTIVATE；
        // 若按默认值激活自己，所属窗口立刻 Deactivate，弹层就在鼠标抬起前被关掉，选项永远选不中。
        // MA_NOACTIVATE(3) 既不让弹层抢激活，也保留这次点击，抬起时照常 Commit。
        protected override void WndProc(ref Message m)
        {
            if (!menu && m.Msg == 0x0021) { m.Result = (IntPtr)3; return; }
            base.WndProc(ref m);
        }
        private void OnOwnerDeactivate(object sender, EventArgs e) { Close(); }
        bool IMessageFilter.PreFilterMessage(ref Message message)
        {
            // Keep the click for its original target, including other controls and title bars.
            switch (message.Msg)
            {
                case 0x0201: case 0x0204: case 0x0207: case 0x020B: // client button down
                case 0x00A1: case 0x00A4: case 0x00A7: case 0x00AB: // non-client button down
                    if (message.HWnd != Handle && message.HWnd != comboAnchor) Close();
                    break;
            }
            return false;
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (comboOwner != null)
                {
                    Application.RemoveMessageFilter(this);
                    comboOwner.Deactivate -= OnOwnerDeactivate;
                    comboOwner = null;
                }
                animation.Dispose();
                backdrop.Dispose();
            }
            base.Dispose(disposing);
        }
        protected override AccessibleObject CreateAccessibilityInstance(){return new PopupAccessible(this);}
        private sealed class PopupAccessible : ControlAccessibleObject
        {
            private readonly UiPopup popup; private readonly AccessibleObject[] children;
            internal PopupAccessible(UiPopup popup):base(popup){this.popup=popup;children=new AccessibleObject[popup.labels.Length];for(int i=0;i<children.Length;i++)children[i]=new ItemAccessible(popup,i);}
            public override int GetChildCount(){return popup.labels.Length;}
            public override AccessibleObject GetChild(int index){return index<0||index>=popup.labels.Length?null:children[index];}
        }
        private sealed class ItemAccessible : AccessibleObject
        {
            private readonly UiPopup popup;private readonly int index;
            internal ItemAccessible(UiPopup popup,int index){this.popup=popup;this.index=index;}
            public override string Name {get{return popup.labels[index];}set{}}
            public override AccessibleRole Role {get{return popup.menu?AccessibleRole.MenuItem:AccessibleRole.ListItem;}}
            public override Rectangle Bounds { get { return popup.RectangleToScreen(popup.RowBounds(index)); } }
            public override AccessibleStates State {get{return AccessibleStates.Selectable | (popup.active==index?AccessibleStates.Selected:AccessibleStates.None);}}
            public override AccessibleObject Parent {get{return popup.AccessibilityObject;}}
            public override string DefaultAction {get{return "Select";}}
            public override void DoDefaultAction(){popup.active=index;popup.Commit();}
        }
    }
    internal sealed class QuotaTipLine
    {
        internal string Label;      // "5 小时" / "7 天"
        internal int Remaining;     // 0-100
        internal string Detail;     // "1 小时 45 分后恢复"
        internal bool Superseded;   // 7 天已耗尽时，5 小时行整行调暗并划掉
        internal string QuotaText
        {
            get { return Remaining <= 0 ? L.T("已耗尽") : L.F("剩余 {0}%", Remaining); }
        }
        internal string Render()
        {
            if (Remaining < 0) return String.IsNullOrEmpty(Detail) ? Label : Label + "｜" + Detail;
            return String.IsNullOrEmpty(Detail) ? Label + " " + QuotaText : Label + " " + QuotaText + "｜" + Detail;
        }
    }

    // WinForms registers legacy tray icons, which can display an empty native tooltip.
    // Version 4 suppresses it by default. Unpack its callbacks for the WinForms window.
    internal sealed class TrayIconHover : NativeWindow
    {
        private const int TrayCallback = 0x800, ContextMenu = 0x7b, MouseMove = 0x200;
        private const int RightDown = 0x204, RightUp = 0x205, PopupOpen = 0x406;
        private const uint AddIcon = 0, ModifyIcon = 1, DeleteIcon = 2, SetVersion = 4, CustomTooltipVersion = 4;
        private const uint NifMessage = 0x01, NifIcon = 0x02;
        internal const uint CleanRegistrationFlags = NifMessage | NifIcon;
        internal const uint IconUpdateFlags = NifIcon;
        private static readonly int TaskbarCreated = RegisterWindowMessage("TaskbarCreated");
        private static readonly System.Reflection.BindingFlags IconFields =
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        private static readonly System.Reflection.FieldInfo IdField = typeof(NotifyIcon).GetField("id", IconFields);
        private static readonly System.Reflection.FieldInfo WindowField = typeof(NotifyIcon).GetField("window", IconFields);
        private static readonly System.Reflection.FieldInfo IconField = typeof(NotifyIcon).GetField("icon", IconFields);
        private readonly NotifyIcon icon;
        private readonly UiTrayMenu menu;
        private uint id;
        private bool useCustom = true;
        private bool version4;
        private bool blanked;
        private string savedTip;
        private DateTime lastAssert;
        internal bool Enabled { get { return version4 && useCustom; } }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct NotifyIconData
        {
            public uint Size;
            public IntPtr Window;
            public uint Id, Flags, Callback;
            public IntPtr Icon;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip;
            public uint State, StateMask;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info;
            public uint Version;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string InfoTitle;
            public uint InfoFlags;
            public Guid Guid;
            public IntPtr BalloonIcon;
        }

        [DllImport("shell32.dll", ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool Shell_NotifyIconW(uint command, ref NotifyIconData data);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int RegisterWindowMessage(string name);

        internal TrayIconHover(NotifyIcon icon, UiTrayMenu menu)
        {
            this.icon = icon;
            this.menu = menu;
        }

        internal static bool TryGetIdentity(NotifyIcon icon, out IntPtr handle, out uint id)
        {
            handle = IntPtr.Zero;
            id = 0;
            if (icon == null || IdField == null || WindowField == null) return false;
            var window = WindowField.GetValue(icon) as NativeWindow;
            if (window == null || window.Handle == IntPtr.Zero) return false;
            handle = window.Handle;
            id = Convert.ToUInt32(IdField.GetValue(icon));
            return true;
        }

        internal void SetFallbackTip(string tip)
        {
            savedTip = tip ?? "";
            if (!useCustom)
            {
                try { if (icon.Text != savedTip) icon.Text = savedTip; } catch { }
            }
        }

        internal void SetIcon(Icon nextIcon)
        {
            if (nextIcon == null) return;
            if (!Enabled || Handle == IntPtr.Zero || IconField == null)
            {
                icon.Icon = nextIcon;
                return;
            }
            // Update WinForms' cached icon reference without calling UpdateIcon(), which
            // hardcodes NIF_TIP and recreates Windows 11 Explorer's empty 28x25 hover box.
            IconField.SetValue(icon, nextIcon);
            var modify = new NotifyIconData
            {
                Size = (uint)Marshal.SizeOf(typeof(NotifyIconData)),
                Window = Handle,
                Id = id,
                Flags = IconUpdateFlags,
                Icon = nextIcon.Handle
            };
            Shell_NotifyIconW(ModifyIcon, ref modify);
        }

        internal void SetVisible(bool visible)
        {
            if (icon.Visible == visible) return;
            if (visible && useCustom)
            {
                if (!blanked && !String.IsNullOrEmpty(icon.Text)) savedTip = icon.Text;
                try { icon.Text = ""; } catch { }
                blanked = true;
            }
            icon.Visible = visible;
            version4 = false;
            if (!visible || !useCustom)
            {
                if (!visible && !useCustom) RestoreFallbackTip();
                return;
            }
            IntPtr window;
            if (!TryGetIdentity(icon, out window, out id)) return;
            if (Handle != window) AssignHandle(window);
            RegisterCleanVersion4();
        }

        internal void Disable()
        {
            useCustom = false;
            if (version4 && ApplyVersion(0)) version4 = false;
            RestoreFallbackTip();
        }

        private void RestoreFallbackTip()
        {
            if (!blanked && String.IsNullOrEmpty(savedTip)) return;
            blanked = false;
            try { icon.Text = savedTip ?? ""; } catch { }
        }

        // WinForms NotifyIcon always calls Shell_NotifyIcon with NIF_TIP (0x04) even when
        // Text is "". Windows 11 Explorer builds an empty 28x25 XAML ToolTip host whenever
        // NIF_TIP is supplied. Replace the icon entry with a clean NIF_MESSAGE | NIF_ICON
        // registration (no NIF_TIP, no NIF_SHOWTIP) and set NOTIFYICON_VERSION_4.
        private void RegisterCleanVersion4()
        {
            var data = new NotifyIconData
            {
                Size = (uint)Marshal.SizeOf(typeof(NotifyIconData)),
                Window = Handle,
                Id = id
            };
            Shell_NotifyIconW(DeleteIcon, ref data);
            Icon current = icon.Icon;
            data.Flags = CleanRegistrationFlags;
            data.Callback = TrayCallback;
            data.Icon = current != null ? current.Handle : IntPtr.Zero;
            Shell_NotifyIconW(AddIcon, ref data);
            version4 = ApplyVersion(CustomTooltipVersion);
        }

        // Explorer can drop the version-4 registration when it rebuilds the notification area,
        // and the window handle can change with it. Re-assert the registration right before a
        // custom tip is shown so the native tooltip (and its empty host popup) stays hidden.
        // Rate limited because MouseMove fires continuously while the pointer stays on the icon.
        internal void Reassert()
        {
            if (!useCustom || !icon.Visible) return;
            DateTime now = DateTime.UtcNow;
            if ((now - lastAssert).TotalMilliseconds < 1000) return;
            Rebind(false);
        }

        // Identity is re-read every time: Explorer rebuilds the notification area on its own
        // schedule and the window handle or icon id can change underneath us.
        private void Rebind(bool forceCleanAdd)
        {
            lastAssert = DateTime.UtcNow;
            IntPtr window;
            uint currentId;
            if (!useCustom || !icon.Visible || !TryGetIdentity(icon, out window, out currentId))
            {
                version4 = false;
                return;
            }
            bool identityChanged = Handle != window || id != currentId;
            id = currentId;
            if (Handle != window)
            {
                if (Handle != IntPtr.Zero) ReleaseHandle();
                AssignHandle(window);
            }
            if (forceCleanAdd || identityChanged || !version4) RegisterCleanVersion4();
            else version4 = ApplyVersion(CustomTooltipVersion);
        }

        private bool ApplyVersion(uint version)
        {
            var data = new NotifyIconData { Size = (uint)Marshal.SizeOf(typeof(NotifyIconData)), Window = Handle, Id = id, Version = version };
            return Shell_NotifyIconW(SetVersion, ref data);
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == TaskbarCreated && useCustom && icon.Visible)
            {
                // Intercept TaskbarCreated before WinForms WmTaskbarCreated sends NIM_ADD with NIF_TIP.
                Rebind(true);
                return;
            }
            if (message.Msg == TrayCallback && version4)
            {
                int eventId = (int)(message.LParam.ToInt64() & 0xffff);
                message.WParam = (IntPtr)id;
                if (eventId == ContextMenu)
                {
                    if (menu.PopupVisible) return;
                    message.LParam = (IntPtr)RightDown;
                    base.WndProc(ref message);
                    eventId = RightUp;
                }
                if (eventId == RightUp && menu.PopupVisible) return;
                if (eventId == PopupOpen) eventId = MouseMove;
                message.LParam = (IntPtr)eventId;
            }
            base.WndProc(ref message);
        }
    }

    // Explorer draws its own translucent host popup while the pointer sits on a tray icon.
    // With NOTIFYICON_VERSION_4 that popup stays hidden; if the registration was lost it can
    // remain as a small dark square peeking out behind the quota tip. Hide only that empty
    // shell: same window class, small, and overlapping the tip we just showed.
    internal static class TrayPopupSuppressor
    {
        private const string HoverShellClass = "Xaml_WindowedPopupClass";
        private static DateTime lastSweep;

        [StructLayout(LayoutKind.Sequential)] private struct RectNative { public int Left, Top, Right, Bottom; }
        private delegate bool EnumProc(IntPtr window, IntPtr parameter);
        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc callback, IntPtr parameter);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, System.Text.StringBuilder text, int length);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out RectNative rect);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr window, int command);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);

        internal static bool ShouldHide(string className, Rectangle shell, Rectangle tip)
        {
            // The host popup can sit just outside the tip edge, so allow a margin.
            Rectangle reach = tip;
            reach.Inflate(48, 48);
            return className == HoverShellClass && shell.Width > 0 && shell.Height > 0 &&
                shell.Width <= 160 && shell.Height <= 120 && reach.IntersectsWith(shell);
        }

        internal static bool Enabled = true;
        // Last protection pass, surfaced by the tray hover sample so the result is visible.
        internal static int LastFound, LastHidden;

        internal static int Hide(Rectangle tip)
        {
            if (!Enabled) return 0;
            DateTime now = DateTime.UtcNow;
            if ((now - lastSweep).TotalMilliseconds < 400) return 0;
            lastSweep = now;
            int found = Sweep(tip, true);
            LastFound = found;
            LastHidden = found;
            return found;
        }

        // Read-only counterpart used by the tray hover sample so the result is checkable.
        internal static int Count(Rectangle tip) { return Sweep(tip, false); }

        private static int Sweep(Rectangle tip, bool hide)
        {
            int found = 0;
            EnumWindows(delegate(IntPtr window, IntPtr parameter)
            {
                if (!IsWindowVisible(window)) return true;
                RectNative rect;
                if (!GetWindowRect(window, out rect)) return true;
                Rectangle shell = Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
                System.Text.StringBuilder name = new System.Text.StringBuilder(64);
                GetClassName(window, name, name.Capacity);
                if (!ShouldHide(name.ToString(), shell, tip)) return true;
                found++;
                if (hide)
                {
                    // Hiding alone can be undone by Explorer; park it off-screen as well.
                    ShowWindow(window, 0);
                    SetWindowPos(window, IntPtr.Zero, -32000, -32000, 0, 0, 0x15);
                }
                return true;
            }, IntPtr.Zero);
            return found;
        }
    }

    internal sealed class UiQuotaTip : Form
    {
        private const int Shadow = 9, PadX = 14, PadY = 10, RowHeight = 22, HeaderHeight = 20;
        private readonly System.Windows.Forms.Timer life = new System.Windows.Forms.Timer();
        private readonly UiLiveBackdrop backdrop;
        private NotifyIcon anchorIcon;
        private string header = "";
        private QuotaTipLine[] lines = new QuotaTipLine[0];
        private DateTime shownAt, lastRenderAt;
        private int dpi = 96;
        [StructLayout(LayoutKind.Sequential)] private struct RectNative { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] private struct IconIdentifier { public int Size; public IntPtr Window; public uint Id; public Guid Guid; }
        [DllImport("shell32.dll")] private static extern int Shell_NotifyIconGetRect(ref IconIdentifier id, out RectNative rect);
        [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT point);
        [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }

        internal UiQuotaTip()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            AccessibleRole = AccessibleRole.ToolTip;
            backdrop = new UiLiveBackdrop(BackdropBounds, delegate { if (!PresentSurface()) Dismiss(); });
            life.Interval = 180;
            life.Tick += delegate { Tick(); };
        }
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x08000000 | 0x00080000 | 0x80 | 0x20; // NoActivate, Layered, ToolWindow, click-through
                return cp;
            }
        }
        internal static Rectangle AnchorBounds(NotifyIcon icon)
        {
            try
            {
                IntPtr window;
                uint iconId;
                if (TrayIconHover.TryGetIdentity(icon, out window, out iconId))
                {
                    IconIdentifier id = new IconIdentifier { Size = Marshal.SizeOf(typeof(IconIdentifier)), Window = window, Id = iconId };
                    RectNative rect;
                    if (Shell_NotifyIconGetRect(ref id, out rect) == 0)
                    {
                        Rectangle bounds = Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
                        if (!bounds.IsEmpty && Screen.FromRectangle(bounds).Bounds.Contains(bounds.Location)) return bounds;
                    }
                }
            }
            catch { }
            Point cursor = Cursor.Position;
            return new Rectangle(cursor.X - 16, cursor.Y - 16, 32, 32);
        }
        internal bool PrepareRefresh(NotifyIcon icon)
        {
            if (!Visible || anchorIcon != icon) return false;
            lastRenderAt = DateTime.MinValue;
            return true;
        }
        internal void Present(NotifyIcon icon, string title, QuotaTipLine[] rows, Rectangle? placementAnchor = null)
        {
            if (rows == null || rows.Length == 0) { Dismiss(); return; }
            DateTime now = DateTime.UtcNow;
            // MouseMove fires continuously over the icon; refresh at most ~8 times a second.
            if (Visible && anchorIcon == icon && (now - lastRenderAt).TotalMilliseconds < 120) { shownAt = now; return; }
            anchorIcon = icon;
            header = title ?? "";
            lines = rows;
            Rectangle anchor = placementAnchor ?? AnchorBounds(icon);
            try { using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) dpi = (int)Math.Round(g.DpiX); } catch { }
            using (Font font = TipFont(9f))
            {
                int width;
                using (Graphics measure = Graphics.FromHwnd(IntPtr.Zero))
                using (StringFormat mf = new StringFormat(StringFormat.GenericDefault))
                {
                    mf.FormatFlags |= StringFormatFlags.NoWrap;
                    width = (int)Math.Ceiling(measure.MeasureString(header, font, PointF.Empty, mf).Width);
                    foreach (QuotaTipLine line in rows)
                        width = Math.Max(width, (int)Math.Ceiling(MeasureRow(measure, font, line, mf)));
                }
                Size size = new Size(width + ScalePixel(PadX) * 2 + ScalePixel(Shadow) * 2,
                    ScalePixel(PadY) * 2 + ScalePixel(HeaderHeight) + rows.Length * ScalePixel(RowHeight) + ScalePixel(Shadow) * 2);
                Rectangle work = Screen.FromRectangle(anchor).WorkingArea;
                Rectangle target = UiPopup.Placement(size, anchor, work, true, ScalePixel(Shadow), ScalePixel(6));
                if (Visible && Bounds != target) Hide();
                Bounds = target;
                if (!Visible) backdrop.CaptureInitial(BackdropBounds());
                if (!PresentSurface()) throw new System.ComponentModel.Win32Exception();
                if (!Visible) { Show(); backdrop.Start(this); }
                // The native hover shell can survive a lost version-4 registration; it would
                // otherwise peek out from under this window as a stray dark square.
                TrayPopupSuppressor.Hide(Bounds);
                shownAt = DateTime.UtcNow;
                lastRenderAt = shownAt;
                life.Start();
            }
        }
        private int ScalePixel(int value) { return Math.Max(1, (int)Math.Round(value * dpi / 96.0)); }
        private Rectangle BackdropBounds() { return Rectangle.Inflate(Bounds, -ScalePixel(Shadow), -ScalePixel(Shadow)); }

        private bool PresentSurface()
        {
            using (Font font = TipFont(9f))
            using (Bitmap bitmap = Render(ScalePixel, font))
                return UiLayeredSurface.Present(Handle, bitmap, Location, 255);
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            if (!Visible && backdrop != null) backdrop.Stop();
            base.OnVisibleChanged(e);
        }
        internal void Dismiss()
        {
            life.Stop();
            anchorIcon = null;
            if (Visible) Hide();
        }
        // Debug preview used by --test-quota-tip; shows the real rendering without a tray icon.
        internal static void ShowPreview(string header, QuotaTipLine[] rows, int milliseconds)
        {
            UiQuotaTip tip = new UiQuotaTip();
            tip.Present(null, header, rows);
            System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
            timer.Interval = milliseconds;
            timer.Tick += delegate { timer.Stop(); tip.Dismiss(); tip.Close(); Application.ExitThread(); };
            timer.Start();
            Application.Run(tip);
        }
        private static Font TipFont(float size) { return UiTheme.UiFont(size, FontStyle.Regular); }
        // Width must match how the row is drawn segment by segment, or the last
        // segment gets trimmed by a few pixels.
        private static float MeasureRow(Graphics g, Font font, QuotaTipLine line, StringFormat format)
        {
            using (Font rowFont = new Font(font, line.Remaining == 0 && !line.Superseded ? FontStyle.Bold : FontStyle.Regular))
            {
                if (line.Remaining <= 0 || line.Superseded) return g.MeasureString(line.Render(), rowFont, PointF.Empty, format).Width;
                float width = g.MeasureString(line.Label + " ", rowFont, PointF.Empty, format).Width;
                width += g.MeasureString(line.QuotaText, rowFont, PointF.Empty, format).Width;
                if (!String.IsNullOrEmpty(line.Detail)) width += g.MeasureString("｜" + line.Detail, rowFont, PointF.Empty, format).Width;
                return width;
            }
        }
        private static float DrawSegment(Graphics g, Font font, Brush ink, string text, float left, RectangleF row, StringFormat format)
        {
            if (String.IsNullOrEmpty(text)) return left;
            float available = Math.Max(1, row.Right - left);
            g.DrawString(text, font, ink, new RectangleF(left, row.Top, available, row.Height), format);
            SizeF size = g.MeasureString(text, font, PointF.Empty, format);
            return left + Math.Min(size.Width, available);
        }
        private void Tick()
        {
            if (!Visible) { life.Stop(); return; }
            // Keep guarding while the tip is up: Explorer may create its hover host a beat
            // after the tip appears, so one sweep at Present time is not enough.
            TrayPopupSuppressor.Hide(Bounds);
            if ((DateTime.UtcNow - shownAt).TotalSeconds > 8) { Dismiss(); return; }
            if (anchorIcon == null) return; // preview mode keeps the tip until its own timer closes it
            POINT cursor;
            if (!GetCursorPos(out cursor)) return;
            Rectangle anchor = AnchorBounds(anchorIcon);
            anchor.Inflate(4, 4);
            if (!anchor.Contains(new Point(cursor.X, cursor.Y))) Dismiss();
        }
        private Bitmap Render(Func<int, int> scale, Font font)
        {
            int shadow = scale(Shadow);
            Bitmap bitmap = new Bitmap(Width, Height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                g.Clear(Color.Transparent);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                RectangleF body = new RectangleF(shadow, shadow, Width - shadow * 2, Height - shadow * 2);
                UiBackdrop.PaintPopup(g, body, backdrop.Frame, scale(UiTheme.Dark ? UiTheme.WindowRadius : UiTheme.Radius), scale, 6, true);
                g.PixelOffsetMode = PixelOffsetMode.Default;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                using (var format = new StringFormat(StringFormat.GenericDefault))
                using (Font titleFont = TipFont(8.5f))
                using (var muted = new SolidBrush(UiTheme.Dark ? UiBackdrop.PopupTextColor : Color.FromArgb(66, 76, 88)))
                using (var normal = new SolidBrush(UiBackdrop.PopupTextColor))
                {
                    format.LineAlignment = StringAlignment.Center;
                    format.FormatFlags |= StringFormatFlags.NoWrap;
                    format.Trimming = StringTrimming.EllipsisCharacter;
                    float x = body.Left + scale(PadX), width = body.Width - scale(PadX) * 2;
                    g.DrawString(header, titleFont, muted, new RectangleF(x, body.Top + scale(PadY - 2), width, scale(HeaderHeight)), format);
                    float y = body.Top + scale(PadY) + scale(HeaderHeight);
                    foreach (QuotaTipLine line in lines)
                    {
                        RectangleF row = new RectangleF(x, y, width, scale(RowHeight));
                        bool exhausted = line.Remaining == 0 && !line.Superseded;
                        bool unknown = line.Remaining < 0;
                        using (Font rowFont = new Font(font, exhausted ? FontStyle.Bold : FontStyle.Regular))
                        {
                            if (line.Superseded)
                            {
                                // Long window is exhausted: dim the 5-hour row and strike through with one smooth line.
                                string rendered = line.Render();
                                Color dimText = UiTheme.Dark ? Color.FromArgb(128, 136, 148) : Color.FromArgb(106, 114, 124);
                                Color strikeColor = UiTheme.Dark ? Color.FromArgb(200, 144, 152, 164) : Color.FromArgb(200, 92, 100, 110);
                                using (var dimBrush = new SolidBrush(dimText))
                                using (var strikePen = new Pen(strikeColor, Math.Max(1.1f, 1.2f * dpi / 96f)))
                                {
                                    g.DrawString(rendered, rowFont, dimBrush, row, format);
                                    float textW = g.MeasureString(rendered, rowFont, PointF.Empty, format).Width;
                                    float midY = (float)Math.Round(row.Top + row.Height * 0.50f) + 0.5f;
                                    g.DrawLine(strikePen, row.Left - 1f, midY, Math.Min(row.Right, row.Left + textW - 2f), midY);
                                }
                            }
                            else if (exhausted || unknown)
                            {
                                // Exhausted keeps the whole line red; unknown stays neutral.
                                using (var rowInk = new SolidBrush(UiTheme.QuotaInk(line.Remaining)))
                                    g.DrawString(line.Render(), rowFont, rowInk, row, format);
                            }
                            else
                            {
                                // Only the "剩余 x%" token carries the colour; the label and
                                // reset time stay in normal text colour.
                                using (var quota = new SolidBrush(UiTheme.QuotaInk(line.Remaining)))
                                {
                                    float cursor = row.Left;
                                    cursor = DrawSegment(g, rowFont, normal, line.Label + " ", cursor, row, format);
                                    cursor = DrawSegment(g, rowFont, quota, line.QuotaText, cursor, row, format);
                                    if (!String.IsNullOrEmpty(line.Detail))
                                        DrawSegment(g, rowFont, normal, "｜" + line.Detail, cursor, row, format);
                                }
                            }
                        }
                        y += scale(RowHeight);
                    }
                }
            }
            return bitmap;
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) { life.Dispose(); backdrop.Dispose(); }
            base.Dispose(disposing);
        }
    }

    internal sealed class UiTrayMenu : ContextMenuStrip
    {
        private NotifyIcon target; private UiPopup popup;
        private bool menuOpen;
        // 菜单开着的时候托盘悬停提示要让位，不然两个窗口会互相顶掉。
        internal bool PopupVisible { get { return menuOpen && popup != null && !popup.IsDisposed; } }
        internal string StatusText;
        internal Rectangle LastAnchor,LastPopupBounds;
        internal int AnimationFrames {get{return popup==null?0:popup.AnimationFrames;}}
        internal void ShowFor(NotifyIcon icon){target=icon;Show(Cursor.Position);}
        [StructLayout(LayoutKind.Sequential)] private struct RectNative {public int Left,Top,Right,Bottom;}
        [StructLayout(LayoutKind.Sequential)] private struct IconIdentifier {public int Size;public IntPtr Window;public uint Id;public Guid Guid;}
        [DllImport("shell32.dll")]private static extern int Shell_NotifyIconGetRect(ref IconIdentifier id,out RectNative rect);
        internal void Track(NotifyIcon icon) {icon.MouseDown+=delegate {target=icon;};}
        private Rectangle IconAnchor()
        {
            try
            {
                var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
                var idField=typeof(NotifyIcon).GetField("id",flags);var windowField=typeof(NotifyIcon).GetField("window",flags);
                if(target!=null&&idField!=null&&windowField!=null)
                {
                    NativeWindow window=windowField.GetValue(target) as NativeWindow;
                    IconIdentifier id=new IconIdentifier {Size=Marshal.SizeOf(typeof(IconIdentifier)),Window=window.Handle,Id=Convert.ToUInt32(idField.GetValue(target))};RectNative rect;
                    if(Shell_NotifyIconGetRect(ref id,out rect)==0)
                    {
                        Rectangle shellRect=Rectangle.FromLTRB(rect.Left,rect.Top,rect.Right,rect.Bottom);
                        Point center=new Point((shellRect.Left+shellRect.Right)/2,(shellRect.Top+shellRect.Bottom)/2);
                        if (Screen.FromPoint(center).Bounds.Contains(center)) return shellRect;
                    }
                }
            }
            catch { }
            Point cursor=Cursor.Position; return new Rectangle(cursor.X-16,cursor.Y-16,32,32);
        }
        protected override void OnOpening(System.ComponentModel.CancelEventArgs e)
        {
            base.OnOpening(e);if(e.Cancel)return;e.Cancel=true;
            UiTheme.RefreshSystem();
            if(popup!=null&&!popup.IsDisposed)popup.Close();
            var items=new List<ToolStripItem>();foreach(ToolStripItem item in Items)if(item.Available&&item.Enabled)items.Add(item);
            if(items.Count==0)return;
            string[] labels=items.ConvertAll<string>(delegate(ToolStripItem item){return item.Text;}).ToArray();
            int exit = items.FindIndex(delegate(ToolStripItem item) { return item.Name == "tray-exit"; });
            popup=new UiPopup(labels,0,true,Font,delegate(int index){items[index].PerformClick();},exit,StatusText);
            LastAnchor=popup.Open(null,IconAnchor,String.IsNullOrEmpty(StatusText)?156:196,true);
            LastPopupBounds=popup.Bounds;
            menuOpen=true;
            popup.FormClosed+=delegate{menuOpen=false;};
        }
        protected override void Dispose(bool disposing){if(disposing&&popup!=null)popup.Dispose();base.Dispose(disposing);}
    }
}
