using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace QuotaTrayApp
{
    internal sealed class SettingsAccount { internal string Slot, Label, Plan, EmailMask, Quota; internal int DisplayNumber; internal WindowInfo Primary, Secondary; internal bool Failed; internal bool Visible; internal int NotifyLevel; }
    internal sealed class SettingsSnapshot
    {
        internal List<SettingsAccount> Accounts;
        internal string DesktopSlot, Language, Theme, AccountReadError;
        internal int DefaultNotifyLevel;
        internal bool AutoStart, AccountsLoading, CodexMissing;
    }
    internal sealed class SettingsActions
    {
        internal Func<SettingsSnapshot> Read;
        internal Action ConnectAccounts, RefreshAll, LocateCodex, CreateShortcut, Exit;
        internal Func<string[], string, bool> SaveDisplay;
        internal Action<string, int> SetAccountLevel;
        internal Func<int, bool> ApplyGlobalLevel;
        internal Action<string> SetLanguage, SetTheme;
        internal Func<bool, bool> SetAutoStart;
    }

    internal sealed class QuotaSettingsForm : UiWindow
    {
        private readonly SettingsActions actions;
        private readonly FlowLayoutPanel sidebar, content;
        private readonly RadioButton[] navigation = new RadioButton[4];
        private bool updatingNavigation, resizingCards;
        private readonly System.Windows.Forms.Timer themeTimer = new System.Windows.Forms.Timer();
        private readonly ToolTip tips = new ToolTip();
        private Label versionLabel;
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
        private const int LayoutGap = 10;
        private const int CardInset = 12;
        private const int ControlHeight = 33;
        private const int ShortSelectWidth = 184;
        private const int LongSelectWidth = 252;
        private const int IconSlot = 16;
        private const int IconLeft = 12;
        private const int TextLeft = 34;
        private const int CardPaddingSize = CardInset;
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
            { "账号提醒档位", new string[] { "アカウント別の通知レベル", "Account alert levels" } },
            { "语言", new string[] { "言語", "Language" } },
            { "开机启动", new string[] { "Windowsの起動時に開始", "Start with Windows" } },
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
            string version = typeof(QuotaSettingsForm).Assembly.GetName().Version.ToString(4);
            string lang = snapshot == null ? "zh" : snapshot.Language;
            if (lang == "en") return "Version " + version;
            if (lang == "ja") return "バージョン " + version;
            return "版本 " + version;
        }

        internal QuotaSettingsForm(SettingsActions actions, string page)
        {
            if (actions == null || actions.Read == null) throw new ArgumentNullException("actions");
            this.actions = actions;
            Text = "额度显示器设置";
            TitlePrimary = "额度显示器"; TitleSecondary = "设置";
            ShowIcon = true;
            AutoScaleMode = AutoScaleMode.Dpi;
            Size area = Screen.FromPoint(Cursor.Position).WorkingArea.Size;
            Size = new Size(Math.Min(800, area.Width), Math.Min(520, area.Height));
            MinimumSize = new Size(Math.Min(680, area.Width), Math.Min(430, area.Height));
            Font = new Font("Microsoft YaHei UI", 10.5f);
            StartPosition = FormStartPosition.CenterScreen;
            sidebar = new FlowLayoutPanel();
            sidebar.Name = "sidebar";
            sidebar.Dock = DockStyle.Left;
            sidebar.Width = 152;
            sidebar.Padding = new Padding(10, 8, 10, 8);
            sidebar.FlowDirection = FlowDirection.TopDown;
            sidebar.WrapContents = false;
            for (int i = 0; i < Pages.Length; i++)
            {
                string target = Pages[i];
                RadioButton nav = new UiNavigation();
                nav.Name = "nav-" + target;
                nav.Appearance = Appearance.Button;
                nav.TextAlign = ContentAlignment.MiddleLeft;
                nav.Size = new Size(132, 36);
                nav.Margin = new Padding(0, 0, 0, 4);
                nav.CheckedChanged += delegate { if (!updatingNavigation && nav.Checked) Navigate(target); };
                navigation[i] = nav;
                sidebar.Controls.Add(nav);
            }
            content = new FlowLayoutPanel();
            content.Name = "content";
            content.Dock = DockStyle.Fill;
            content.FlowDirection = FlowDirection.TopDown;
            content.WrapContents = false;
            content.AutoScroll = true;
            content.Padding = new Padding(18, 15, 18, 14);
            Controls.Add(content);
            Controls.Add(sidebar);
            versionLabel = new Label();
            versionLabel.Name = "version-label";
            versionLabel.AutoSize = true;
            versionLabel.Text = VersionCaption();
            Font versionFont = new Font(Font.FontFamily, 8.5f);
            versionLabel.Font = versionFont;
            versionLabel.Disposed += delegate { versionFont.Dispose(); };
            versionLabel.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;
            versionLabel.Location = new Point(18, Math.Max(0, ClientSize.Height - versionLabel.PreferredHeight - 11));
            Controls.Add(versionLabel);
            versionLabel.BringToFront();
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
            UiTheme.Hook(this);
            themeTimer.Interval = 2000;
            themeTimer.Tick += delegate { UiTheme.RefreshSystem(); };
            themeTimer.Start();
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
            Point oldScroll = content.AutoScrollPosition;
            snapshot = current ?? ReadSnapshot();
            accountQuotaRows.Clear();
            Text = T("额度显示器设置");
            TitlePrimary = T("额度显示器"); TitleSecondary = T("设置"); TitleBar.Invalidate();
            if (versionLabel != null) versionLabel.Text = VersionCaption();
            content.SuspendLayout();
            while (content.Controls.Count > 0) content.Controls[0].Dispose();
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
            UiTheme.Apply(this);
            content.ResumeLayout();
            ResizeCards();
            content.PerformLayout();
            content.AutoScrollPosition = preserveScroll ? new Point(-oldScroll.X, -oldScroll.Y) : Point.Empty;
        }

        private GroupBox Card(string slot = null)
        {
            GroupBox card = new UiCard { AccountSlot = slot }; card.AutoSize = false;
            card.Padding = new Padding(CardPaddingSize);
            card.Margin = new Padding(0, 0, 0, LayoutGap);
            FlowLayoutPanel inner = new FlowLayoutPanel(); inner.Name = "inner"; inner.AutoSize = false; inner.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            inner.FlowDirection = FlowDirection.TopDown;
            inner.WrapContents = false; inner.Location = new Point(CardPaddingSize, CardPaddingSize); card.Controls.Add(inner); content.Controls.Add(card);
            return card;
        }
        private FlowLayoutPanel ActionRow()
        {
            FlowLayoutPanel row = new FlowLayoutPanel(); row.Name = "action-row"; row.AutoSize = true; row.AutoSizeMode = AutoSizeMode.GrowAndShrink;
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
                int width = Math.Max(250, content.ClientSize.Width - content.Padding.Horizontal);
                foreach (Control c in content.Controls)
                {
                    GroupBox card = c as GroupBox;
                    if (card == null) { c.MaximumSize = new Size(width, 0); continue; }
                    FlowLayoutPanel inner = Inner(card);
                    int innerWidth = Math.Max(100, width - card.Padding.Horizontal);
                    foreach (Control child in inner.Controls)
                    {
                        Label label = child as Label;
                        if (label != null) label.MaximumSize = new Size(innerWidth - 8, 0);
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
                        if (child is QuotaMeter) child.Width = innerWidth - 6;
                    }
                    inner.Width = innerWidth;
                    inner.PerformLayout();
                    int height = inner.GetPreferredSize(new Size(innerWidth, 0)).Height;
                    inner.Size = new Size(innerWidth, height);
                    card.Size = new Size(width, height + card.Padding.Vertical);
                }
            }
            finally
            {
                content.ResumeLayout(true);
                resizingCards = false;
            }
        }
        private Label TextLine(string text, bool bold)
        {
            Label l = new Label(); l.Text = T(text); l.AutoSize = true; l.MaximumSize = new Size(760, 0);
            l.Margin = new Padding(0, 0, 0, LayoutGap); if (bold) l.Font = new Font(Font, FontStyle.Bold); return l;
        }
        private Button Button(string text, bool accent, EventHandler click)
        {
            UiButton b = new UiButton(); b.Accent = accent; b.Text = T(text); b.AutoSize = true;
            b.MinimumSize = new Size(100, ControlHeight); b.Height = ControlHeight; b.Margin = new Padding(0, 0, LayoutGap, 0); b.Click += click; return b;
        }
        private FlowLayoutPanel Row()
        {
            FlowLayoutPanel row = new FlowLayoutPanel(); row.AutoSize = true; row.AutoSizeMode = AutoSizeMode.GrowAndShrink;
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
            FlowLayoutPanel row = new FlowLayoutPanel(); row.Name = "account-header"; row.AutoSize = true; row.AutoSizeMode = AutoSizeMode.GrowAndShrink; row.WrapContents = false; row.Margin = new Padding(0, 0, 0, LayoutGap);
            Label number = new Label(); number.AutoSize = true; number.Text = AccountNumber(account); number.Name = "account-number"; number.Tag = account.Slot; number.Font = new Font(Font, FontStyle.Bold); number.Margin = new Padding(0, 0, 8, 0); row.Controls.Add(number);
            Label divider = new Label(); divider.AutoSize = true; divider.Text = "|"; divider.ForeColor = UiTheme.Border; divider.Margin = new Padding(0, 0, 8, 0); row.Controls.Add(divider);
            Label plan = new Label(); plan.AutoSize = true; plan.Text = String.IsNullOrEmpty(account.Plan) ? "" : account.Plan; plan.ForeColor = UiTheme.TextColor; plan.Font = new Font(Font, FontStyle.Bold); plan.Margin = new Padding(0, 0, 8, 0); if (!String.IsNullOrEmpty(plan.Text)) row.Controls.Add(plan);
            Label email = new Label(); email.AutoSize = true; email.Text = account.EmailMask ?? ""; email.ForeColor = UiTheme.Muted; email.Margin = new Padding(0); if (!String.IsNullOrEmpty(email.Text)) row.Controls.Add(email);
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

            WindowInfo shortWindow = null, longWindow = null;
            foreach (WindowInfo window in new[] { account.Primary, account.Secondary })
            {
                if (!QuotaMeter.Valid(window)) continue;
                if (window.windowMins > 1440) longWindow = window; else shortWindow = window;
            }
            int count = shortWindow != null && longWindow != null ? 2 : 1;
            changed |= row.Controls.Count != count;
            while (row.Controls.Count > count) row.Controls[row.Controls.Count - 1].Dispose();
            for (int i = 0; i < count; i++)
            {
                WindowInfo window = i == 0 && shortWindow != null ? shortWindow : longWindow;
                string title = window == null ? L.T("正在读取额度…") : window == shortWindow ? L.T("5 小时额度") :
                    L.F("{0} 天额度", (int)Math.Round(window.windowMins / 1440));
                if (i < row.Controls.Count) ((QuotaMeter)row.Controls[i]).UpdateQuota(window, title);
                else
                {
                    var meter = new QuotaMeter(window, title);
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
            error = TextLine("", false); error.Name = "display-error"; error.AutoSize = false; error.Height = 0; error.Margin = new Padding(0); inner.Controls.Add(error);
            Button save = Button("保存显示设置", true, SaveDisplay); save.Name = "save-display"; inner.Controls.Add(save);
        }
        private void SetDisplayError(string text)
        {
            error.Text = text ?? ""; error.Height = String.IsNullOrEmpty(error.Text) ? 0 : 24; error.AutoSize = false;
        }
        private void SaveDisplay(object sender, EventArgs e)
        {
            List<string> selected = new List<string>();
            foreach (CheckBox toggle in displayToggles) if (toggle.Checked) selected.Add((string)toggle.Tag);
            if (selected.Count == 0) { SetDisplayError(T("请至少选择一个显示账号。")); return; }
            if (actions.SaveDisplay == null || !actions.SaveDisplay(selected.ToArray(), desktopSlots[desktop.SelectedIndex]))
            { SetDisplayError(T("保存失败，请重试。")); return; }
            SetDisplayError(T("已保存。"));
        }
        private FlowLayoutPanel ChoiceRow(string name, string[] labels, int selected, Action<int> choose, string[] descriptions = null, string accountSlot = null)
        {
            FlowLayoutPanel row = ActionRow();
            row.Name = name;
            int width = ChoiceWidth();
            for (int i = 0; i < labels.Length; i++)
            {
                int index = i;
                var option = new UiChoice { AccountSlot = accountSlot, Text = T(labels[i]), Size = new Size(width, ControlHeight), Checked = i == selected };
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
            int width = 88;
            foreach (string label in Levels.Concat(Appearances))
            {
                width = Math.Max(width, TextRenderer.MeasureText(label, Font).Width + 16);
                foreach (string translation in Translations[label])
                    width = Math.Max(width, TextRenderer.MeasureText(translation, Font).Width + 16);
            }
            return width;
        }

        private FlowLayoutPanel LevelChoices(string name, int level, Action<int> set, string accountSlot = null)
        {
            return ChoiceRow(name, Levels, Math.Max(0, Math.Min(3, level + 1)),
                delegate(int selected) { if (set != null) set(selected - 1); }, LevelTips, accountSlot);
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
            apply.Size = new Size(100, ControlHeight);
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
            }
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
        }
        private void EqualizeActions(FlowLayoutPanel row)
        {
            int width = 100;
            foreach (UiButton button in row.Controls)
                width = Math.Max(width, TextRenderer.MeasureText(button.Text, Font).Width + (String.IsNullOrEmpty(button.IconKind) ? 32 : 58));
            foreach (UiButton button in row.Controls)
            {
                button.AutoSize = false;
                button.Size = new Size(width, ControlHeight);
                button.Margin = new Padding(0, 0, LayoutGap, 6);
            }
            row.Controls[row.Controls.Count - 1].Margin = Padding.Empty;
        }

        protected override void Dispose(bool disposing) { if (disposing) { tips.Dispose(); themeTimer.Dispose(); } base.Dispose(disposing); }
    }
}


namespace QuotaTrayApp
{
    // Shared geometry and palette for settings and its owned dialogs. Tray drawing stays separate.
    internal static class UiTheme
    {
        internal const int WindowRadius = 8;
        internal const int Radius = 6;
        internal const int CardRadius = 10;
        internal const int CardInnerRadius = 9;
        internal static string Mode = "auto";
        internal static bool Dark;
        internal static event EventHandler Changed;
        internal static Color Background { get { return Dark ? Color.FromArgb(31, 31, 31) : Color.FromArgb(244, 247, 245); } }
        internal static Color Sidebar { get { return Dark ? Color.FromArgb(38, 38, 38) : Color.FromArgb(230, 237, 232); } }
        internal static Color WarningText { get { return Dark ? Color.FromArgb(252, 154, 158) : Color.FromArgb(161, 35, 52); } }
        internal static Color Surface { get { return Dark ? Color.FromArgb(47, 47, 47) : Color.White; } }
        internal static Color Input { get { return Dark ? Color.FromArgb(57, 57, 57) : Color.FromArgb(239, 242, 246); } }
        internal static Color TextColor { get { return Dark ? Color.FromArgb(242, 242, 242) : Color.FromArgb(32, 40, 49); } }
        internal static Color Muted { get { return Dark ? Color.FromArgb(176, 182, 190) : Color.FromArgb(97, 109, 121); } }
        internal static Color Accent { get { return Dark ? Color.FromArgb(125, 221, 184) : Color.FromArgb(23, 107, 84); } }
        internal static Color Selection { get { return Dark ? Color.FromArgb(40, 68, 58) : Color.FromArgb(204, 228, 216); } }
        internal static Color Border { get { return Dark ? Color.FromArgb(70, 74, 78) : Color.FromArgb(212, 219, 226); } }
        // Account colors belong to slots a–e and stay stable when other slots are removed.
        private static readonly Color[] DarkAccounts = {
            Color.FromArgb(125, 221, 184), Color.FromArgb(123, 191, 250), Color.FromArgb(195, 164, 251),
            Color.FromArgb(240, 193, 108), Color.FromArgb(242, 155, 199)
        };
        private static readonly Color[] LightAccounts = {
            Color.FromArgb(23, 107, 84), Color.FromArgb(26, 97, 161), Color.FromArgb(111, 70, 169),
            Color.FromArgb(136, 88, 18), Color.FromArgb(148, 56, 107)
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
        internal static void Present(Form form)
        {
            if (form.WindowState == FormWindowState.Minimized) form.WindowState = FormWindowState.Normal;
            form.Show();
            // STARTUPINFO SW_HIDE can hide the first WinForms Show even though Visible is true.
            ShowWindow(form.Handle, 9);
            form.BringToFront(); form.Activate();
        }
        private static void Frame(Form form)
        {
            if (!form.IsHandleCreated) return;
            bool systemCorners = false;
            try
            {
                int dark = Dark ? 1 : 0, corner = 2;
                DwmSetWindowAttribute(form.Handle, 20, ref dark, 4);
                DwmSetWindowAttribute(form.Handle, 19, ref dark, 4);
                systemCorners = DwmSetWindowAttribute(form.Handle, 33, ref corner, 4) == 0;
            }
            catch (DllNotFoundException) { } catch (EntryPointNotFoundException) { }
            UiWindow window = form as UiWindow;
            if (window != null) window.UseLocalCorners(!systemCorners);
        }
        internal static void ClipRoundedWindow(Form form, int radius)
        {
            using (GraphicsPath shape = Round(new RectangleF(0, 0, form.Width, form.Height), radius))
                form.Region = new Region(shape);
        }
        internal static void Hook(Form form)
        {
            EventHandler apply = delegate { if (!form.IsDisposed) { Apply(form); Frame(form); } };
            Changed += apply;
            form.HandleCreated += delegate { Frame(form); };
            form.Shown += delegate { Apply(form); Frame(form); };
            form.Disposed += delegate { Changed -= apply; };
            Apply(form);
        }
        internal static void Apply(Control root)
        {
            root.ForeColor = TextColor;
            if (root is Form) root.BackColor = Background;
            else if (root.Name == "sidebar" || root.Name == "version-label") root.BackColor = Sidebar;
            else if (root is UiCard) root.BackColor = ((UiCard)root).SurfaceColor;
            else if (root is UiComboBox) root.BackColor = Input;
            else if (root is TextBox) root.BackColor = Surface;
            else root.BackColor = root.Parent == null ? Background : root.Parent.BackColor;
            if (root.Name == "page-caption" || root.Name == "global-level-help" || root.Name == "global-level-error" || root.Name == "min-visible-accounts") root.ForeColor = Muted;
            if (root.Name == "account-number") root.ForeColor = AccountColor(root.Tag as string);
            if (root.Name == "account-status-warning") root.ForeColor = WarningText;
            if (root.Name == "version-label") root.ForeColor = Dark ? Color.FromArgb(120, 123, 124) : Color.FromArgb(117, 126, 120);
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
        internal static void Card(Graphics g, Rectangle r, Color fill, Color border)
        {
            if(r.Width<4||r.Height<4)return;
            g.SmoothingMode=SmoothingMode.AntiAlias; g.PixelOffsetMode=PixelOffsetMode.HighQuality; g.CompositingQuality=CompositingQuality.GammaCorrected;
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
                edge = Blend(background, color, 195);
            }
            if (selected)
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
        internal UiCard() { SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true); }
        internal string AccountSlot { get; set; }
        internal virtual Color SurfaceColor
        {
            get { return AccountSlot == null ? UiTheme.Surface : UiTheme.Blend(UiTheme.Surface, UiTheme.AccountColor(AccountSlot), UiTheme.Dark ? 16 : 12); }
        }
        protected virtual Color BorderColor
        {
            get { return AccountSlot == null ? UiTheme.Border : UiTheme.Blend(UiTheme.Border, UiTheme.AccountColor(AccountSlot), 132); }
        }
        protected override void OnPaintBackground(PaintEventArgs e) { e.Graphics.Clear(Parent == null ? UiTheme.Background : Parent.BackColor); }
        protected override void OnPaint(PaintEventArgs e)
        {
            UiTheme.Card(e.Graphics,ClientRectangle,SurfaceColor,BorderColor);
            if (!String.IsNullOrEmpty(Text)) using (Font bold = new Font(Font,FontStyle.Bold)) TextRenderer.DrawText(e.Graphics,Text,bold,new Rectangle(14,14,Width-28,28),UiTheme.TextColor,TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
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
            e.Graphics.Clear(Parent == null ? UiTheme.Background : Parent.BackColor);
            UiTheme.Material(e.Graphics,ClientRectangle,BackColor,Accent,Danger,hover,pressed,Enabled);
            Color foreground = !Enabled ? UiTheme.Muted : Accent ? (UiTheme.Dark ? Color.FromArgb(14,33,42) : Color.White) : Danger ? Color.White : UiTheme.TextColor;
            Rectangle textRect = ClientRectangle;
            if (!String.IsNullOrEmpty(IconKind))
            {
                Size textSize = TextRenderer.MeasureText(Text, Font, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
                int groupWidth = 16 + 6 + textSize.Width;
                int iconLeft = CenterIconGroup ? Math.Max(12, (Width - groupWidth) / 2) : 12;
                UiSymbols.Action(e.Graphics, IconKind, new Rectangle(iconLeft, (Height - 16) / 2, 16, 16), foreground);
                textRect = new Rectangle(iconLeft + 22, 0, Math.Max(1, Width - iconLeft - 28), Height);
                TextRenderer.DrawText(e.Graphics,Text,Font,textRect,foreground,TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            }
            else TextRenderer.DrawText(e.Graphics,Text,Font,textRect,foreground,TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            if(Focused && ShowFocusCues && UiInput.Keyboard) UiTheme.Focus(e.Graphics,ClientRectangle);
        }
        internal Rectangle IconBounds()
        {
            if (String.IsNullOrEmpty(IconKind)) return Rectangle.Empty;
            Size textSize = TextRenderer.MeasureText(Text, Font, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
            int groupWidth = 16 + 6 + textSize.Width;
            int iconLeft = CenterIconGroup ? Math.Max(12, (Width - groupWidth) / 2) : 12;
            return new Rectangle(iconLeft, (Height - 16) / 2, 16, 16);
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
            e.Graphics.Clear(BackColor);
            UiTheme.Material(e.Graphics, ClientRectangle, BackColor, false, false, hover, pressed, Enabled, Checked, UiTheme.AccountColor(AccountSlot));
            Rectangle text = ClientRectangle;
            text.Inflate(-8, 0);
            TextRenderer.DrawText(e.Graphics, Text, Font, text, Enabled ? UiTheme.TextColor : UiTheme.Muted,
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
            e.Graphics.Clear(Parent.BackColor);
            if(Checked || hover) UiTheme.Fill(e.Graphics,ClientRectangle,Checked ? UiTheme.Selection : UiTheme.Input,null);
            UiSymbols.Navigation(e.Graphics,Name,new Rectangle(9,(Height-16)/2,16,16),Checked ? UiTheme.Accent : UiTheme.Muted);
            TextRenderer.DrawText(e.Graphics,Text,Font,new Rectangle(30,0,Width-36,Height),Checked ? UiTheme.Accent : UiTheme.TextColor,
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
        public override Size GetPreferredSize(Size proposedSize){Size s=TextRenderer.MeasureText(Text,Font);return new Size(s.Width+54,Math.Max(29,s.Height+7));}
        protected override void OnCheckedChanged(EventArgs e)
        {
            from=position;to=Checked?1:0;start=DateTime.UtcNow;if(IsHandleCreated)timer.Start();else position=to;base.OnCheckedChanged(e);Invalidate();
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent.BackColor);e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
            Rectangle track=new Rectangle(3,(Height-20)/2,36,20);
            Color off=UiTheme.Blend(BackColor,UiTheme.Dark?Color.White:Color.Black,UiTheme.Dark?47:64);
            Color on=UiTheme.Blend(BackColor,UiTheme.AccountColor(Tag as string),210);
            if(!Enabled) { off=UiTheme.Blend(BackColor,UiTheme.Dark?Color.White:Color.Black,25); on=UiTheme.Blend(BackColor,UiTheme.AccountColor(Tag as string),105); }
            Color fill=UiTheme.Blend(off,on,(int)(position*255));
            using(GraphicsPath path=UiTheme.Round(track,10))using(LinearGradientBrush brush=new LinearGradientBrush(track,ControlPaint.Light(fill,.12f),fill,90))
            {e.Graphics.FillPath(brush,path);using(Pen edge=new Pen(UiTheme.Border,1))e.Graphics.DrawPath(edge,path);}
            Rectangle knob=new Rectangle(track.Left+3+(int)Math.Round(position*16),track.Top+3,14,14);
            using(Brush shadow=new SolidBrush(Color.FromArgb(60,0,0,0)))e.Graphics.FillEllipse(shadow,knob.X,knob.Y+1,knob.Width,knob.Height);
            using(LinearGradientBrush brush=new LinearGradientBrush(knob,Color.White,Color.FromArgb(219,224,229),90))e.Graphics.FillEllipse(brush,knob);
            TextRenderer.DrawText(e.Graphics,Text,Font,new Rectangle(48,0,Width-48,Height),Enabled?UiTheme.TextColor:UiTheme.Muted,TextFormatFlags.Left|TextFormatFlags.VerticalCenter);
            if(Focused&&UiInput.Keyboard)UiTheme.Focus(e.Graphics,ClientRectangle);
        }
        protected override void Dispose(bool disposing){if(disposing)timer.Dispose();base.Dispose(disposing);}
    }
    internal sealed class QuotaMeter : Control
    {
        private WindowInfo quota;
        private string title, reset;
        internal static bool Valid(WindowInfo w) { return w!=null && !Double.IsNaN(w.used) && !Double.IsInfinity(w.used) && w.used>=0 && w.used<=100 && w.windowMins>0; }
        internal QuotaMeter(WindowInfo quota,string title)
        {
            Name="quota-meter"; Size=new Size(420,92); Margin=new Padding(0);
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw,true);
            AccessibleRole = AccessibleRole.Graphic;
            UpdateQuota(quota, title);
        }

        internal void UpdateQuota(WindowInfo window, string label)
        {
            if (!Valid(window)) window = null;
            string nextReset = window == null ? L.T("暂无") : TrayContext.MultiTooltipCountdown(window.resetsAt);
            if (title == label && reset == nextReset && (quota == null ? window == null : window != null && quota.used == window.used)) return;
            quota = window; title = label; reset = nextReset;
            Text = title + " · " + (quota == null ? L.T("暂无") : L.F("剩余 {0}%", Math.Max(0, 100 - (int)Math.Round(quota.used))));
            AccessibleName = Text;
            Invalidate();
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent == null ? UiTheme.Surface : Parent.BackColor);
            double remaining=quota==null ? 0 : Math.Max(0,Math.Min(100,100-quota.used));
            Color color=quota==null ? UiTheme.Muted : UiTheme.QuotaColor(remaining);
            TextRenderer.DrawText(e.Graphics,title+" · "+L.T("剩余"),Font,new Rectangle(0,0,Width,20),UiTheme.Muted,TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            string amount=quota==null ? "—" : ((int)Math.Round(remaining))+"%";
            using(Font large=new Font(Font.FontFamily,22f,FontStyle.Bold)) TextRenderer.DrawText(e.Graphics,amount,large,new Rectangle(0,19,Width,40),UiTheme.TextColor,TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            Rectangle track=new Rectangle(0,61,Width,5); UiTheme.QuotaGradient(e.Graphics,track,UiTheme.Input);
            if(quota!=null) { int filled=(int)Math.Round(track.Width*remaining/100); if(filled>1) UiTheme.QuotaGradient(e.Graphics,new Rectangle(track.X,track.Y,filled,track.Height),color); }
            TextRenderer.DrawText(e.Graphics,reset,Font,new Rectangle(0,69,Width,22),UiTheme.Muted,TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
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
        internal static void Action(Graphics g, string kind, Rectangle r, Color color)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (Pen p = new Pen(color, 1.6f))
            {
                if (kind == "shortcut")
                {
                    g.DrawRectangle(p, r.X + 1, r.Y + 2, 12, 9);
                    g.DrawLine(p, r.X + 4, r.Y + 14, r.X + 10, r.Y + 14);
                    g.DrawLine(p, r.X + 7, r.Y + 11, r.X + 7, r.Y + 14);
                    g.DrawLine(p, r.X + 10, r.Y + 3, r.X + 14, r.Y + 3);
                    g.DrawLine(p, r.X + 14, r.Y + 3, r.X + 14, r.Y + 7);
                    g.DrawLine(p, r.X + 14, r.Y + 3, r.X + 10, r.Y + 7);
                }
                else if (kind == "codex")
                {
                    g.DrawRectangle(p, r.X + 1, r.Y + 1, 13, 13);
                    g.DrawLine(p, r.X + 4, r.Y + 7, r.X + 6, r.Y + 9);
                    g.DrawLine(p, r.X + 6, r.Y + 9, r.X + 10, r.Y + 5);
                }
                else
                {
                    g.DrawRectangle(p, r.X + 5, r.Y + 1, 9, 13);
                    g.DrawLine(p, r.X + 1, r.Y + 8, r.X + 9, r.Y + 8);
                    g.DrawLine(p, r.X + 6, r.Y + 5, r.X + 9, r.Y + 8);
                    g.DrawLine(p, r.X + 6, r.Y + 11, r.X + 9, r.Y + 8);
                }
            }
        }
        internal static void Navigation(Graphics g,string name,Rectangle r,Color color)
        {
            g.SmoothingMode=SmoothingMode.AntiAlias;
            using(Pen p=new Pen(color,1.6f))
            {
                float x=r.X,y=r.Y,w=r.Width;
                if(name.Contains("accounts")) { g.DrawEllipse(p,x+4,y+1,8,8); g.DrawArc(p,x+1,y+9,14,9,180,180); }
                else if(name.Contains("display")) { g.DrawRectangle(p,x+1,y+2,w-2,10); g.DrawLine(p,x+8,y+12,x+8,y+15); g.DrawLine(p,x+4,y+15,x+12,y+15); }
                else if(name.Contains("notifications")) { g.DrawArc(p,x+4,y+1,8,10,180,180); g.DrawLine(p,x+4,y+6,x+2,y+12); g.DrawLine(p,x+12,y+6,x+14,y+12); g.DrawLine(p,x+2,y+12,x+14,y+12); g.DrawArc(p,x+6,y+12,4,4,0,180); }
                else if(name.Contains("refresh")) { g.DrawArc(p,x+2,y+2,12,12,30,290); g.DrawLines(p,new PointF[] {new PointF(x+11,y),new PointF(x+14,y+4),new PointF(x+9,y+4)}); }
                else { g.DrawEllipse(p,x+3,y+3,10,10); g.DrawEllipse(p,x+6,y+6,4,4); for(int i=0;i<8;i++) { double a=i*Math.PI/4;g.DrawLine(p,x+8+(float)Math.Cos(a)*6,y+8+(float)Math.Sin(a)*6,x+8+(float)Math.Cos(a)*8,y+8+(float)Math.Sin(a)*8); } }
            }
        }
    }
    internal class UiWindow : Form
    {
        internal const int CaptionHeight=42;
        internal readonly UiTitleBar TitleBar;
        internal string TitlePrimary, TitleSecondary;

        internal bool CanResize = true;
        private bool localCorners;
        private UiCornerEdges cornerEdges;
        [StructLayout(LayoutKind.Sequential)] private struct PointNative { public int X,Y; }
        [StructLayout(LayoutKind.Sequential)] private struct MinMax { public PointNative Reserved,MaxSize,MaxPosition,MinTrack,MaxTrack; }
        [DllImport("user32.dll")] internal static extern bool ReleaseCapture();
        [DllImport("user32.dll")] internal static extern IntPtr SendMessage(IntPtr hwnd,int msg,IntPtr w,IntPtr l);
        internal UiWindow()
        {
            FormBorderStyle=FormBorderStyle.None;
            UiInput.Install(); SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw,true);
            AutoScaleDimensions=new SizeF(96,96); AutoScaleMode=AutoScaleMode.Dpi;
            Padding=new Padding(0,CaptionHeight,0,0);
            try { Icon=Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { Icon=SystemIcons.Application; }
            TitleBar=new UiTitleBar(this); TitleBar.Name="window-title"; Controls.Add(TitleBar);
            SizeChanged+=delegate { TitleBar.SetBounds(0,0,ClientSize.Width,CaptionHeight); TitleBar.BringToFront(); };
            TextChanged+=delegate { TitleBar.Invalidate(); };
        }
        protected override CreateParams CreateParams { get { CreateParams p=base.CreateParams; p.Style |= 0x00040000; return p; } }
        protected override void OnShown(EventArgs e) { TitleBar.SetBounds(0,0,ClientSize.Width,CaptionHeight); TitleBar.BringToFront(); base.OnShown(e); }
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
                long value=m.LParam.ToInt64(); Point point=PointToClient(new Point((short)(value & 0xffff),(short)((value>>16)&0xffff)));
                bool left=point.X<6,right=point.X>=ClientSize.Width-6,top=point.Y<6,bottom=point.Y>=ClientSize.Height-6;
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
            close.SetBounds(Width-46,0,46,Height);maximize.SetBounds(Width-92,0,46,Height);minimize.SetBounds(Width-138,0,46,Height);
            maximize.Visible=window.MaximizeBox; minimize.Visible=window.MinimizeBox;
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(UiTheme.Background);
            Control sidebar = window.Controls["sidebar"];
            if (sidebar != null)
                using (Brush fill = new SolidBrush(UiTheme.Sidebar)) e.Graphics.FillRectangle(fill, 0, 0, sidebar.Right, Height);
            if(window.Icon!=null)
            {
                e.Graphics.InterpolationMode=InterpolationMode.HighQualityBicubic; e.Graphics.PixelOffsetMode=PixelOffsetMode.HighQuality;
                using(Bitmap icon=Bitmap.FromHicon(window.Icon.Handle)) e.Graphics.DrawImage(icon,new Rectangle(15,(Height-18)/2,18,18));
            }
            string primary=String.IsNullOrEmpty(window.TitlePrimary)?window.Text:window.TitlePrimary;
            string secondary=window.TitleSecondary;
            using(Font main=new Font(window.Font.FontFamily, String.IsNullOrEmpty(secondary) ? 10.5f : 9.3f, FontStyle.Bold))
            using(Font sub=new Font(window.Font.FontFamily, 8.0f, FontStyle.Regular))
            {
                const TextFormatFlags measureFlags = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;
                const TextFormatFlags drawFlags = measureFlags | TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter;
                Size unconstrained = new Size(Int32.MaxValue, Int32.MaxValue);
                Size mainSize = TextRenderer.MeasureText(e.Graphics, primary, main, unconstrained, measureFlags);
                Size subSize = String.IsNullOrEmpty(secondary) ? Size.Empty : TextRenderer.MeasureText(e.Graphics, secondary, sub, unconstrained, measureFlags);
                Rectangle mainBounds, subBounds;
                int captionStart = minimize.Visible ? minimize.Left : maximize.Visible ? maximize.Left : close.Left;
                PlaceTitle(mainSize, subSize, Height, Math.Min(captionStart, sidebar == null ? Width : sidebar.Right) - 12, out mainBounds, out subBounds);
                TextRenderer.DrawText(e.Graphics, primary, main, mainBounds, UiTheme.TextColor, drawFlags);
                if (!subBounds.IsEmpty)
                    TextRenderer.DrawText(e.Graphics, secondary, sub, subBounds, UiTheme.Muted, drawFlags);
            }
        }

        internal static void PlaceTitle(Size primary, Size secondary, int height, int right, out Rectangle main, out Rectangle sub)
        {
            const int left = 42, gap = 8;
            int width = Math.Max(1, right - left);
            sub = Rectangle.Empty;
            if (secondary.IsEmpty || primary.Width + gap + secondary.Width <= width)
            {
                main = new Rectangle(left, 0, Math.Min(primary.Width + 2, width), height);
                if (!secondary.IsEmpty) sub = new Rectangle(left + primary.Width + gap, 0, width - primary.Width - gap, height);
                return;
            }
            const int padding = 5;
            int top = Math.Max(padding, (height - primary.Height - secondary.Height) / 2);
            int bottom = Math.Max(top, height - padding);
            main = new Rectangle(left, top, width, Math.Min(primary.Height, Math.Max(0, bottom - top - secondary.Height)));
            sub = new Rectangle(left, main.Bottom, width, Math.Min(secondary.Height, bottom - main.Bottom));
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
            e.Graphics.Clear(hover ? kind=="close" ? Color.FromArgb(176,57,74) : UiTheme.Input : UiTheme.Background);
            int x=Width/2,y=Height/2;
            using(Pen p=new Pen(hover && kind=="close"?Color.White:UiTheme.TextColor,1))
            { if(kind=="minimize")e.Graphics.DrawLine(p,x-5,y,x+5,y);else if(kind=="maximize")e.Graphics.DrawRectangle(p,x-4,y-4,8,8);else {e.Graphics.DrawLine(p,x-4,y-4,x+4,y+4);e.Graphics.DrawLine(p,x+4,y-4,x-4,y+4);} }
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
            SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.Selectable|ControlStyles.ResizeRedraw,true);
            Size=new Size(184,33);Margin=new Padding(0,0,0,10);TabStop=true;AccessibleRole=AccessibleRole.ComboBox;Cursor=Cursors.Hand;
        }
        protected override bool IsInputKey(Keys keyData) {Keys k=keyData&Keys.KeyCode;return k==Keys.Up||k==Keys.Down||k==Keys.Home||k==Keys.End||base.IsInputKey(keyData);}
        protected override void OnMouseEnter(EventArgs e){hover=true;Invalidate();base.OnMouseEnter(e);}
        protected override void OnMouseLeave(EventArgs e){hover=false;Invalidate();base.OnMouseLeave(e);}
        protected override void OnMouseDown(MouseEventArgs e){base.OnMouseDown(e);if(e.Button==MouseButtons.Left){UiInput.Keyboard=false;Focus();Open();}}
        protected override void OnGotFocus(EventArgs e){Invalidate();base.OnGotFocus(e);}
        protected override void OnLostFocus(EventArgs e){Invalidate();base.OnLostFocus(e);}
        protected override void OnKeyDown(KeyEventArgs e)
        {
            UiInput.Keyboard=true;
            if(e.KeyCode==Keys.Space||e.KeyCode==Keys.Enter||e.KeyCode==Keys.F4||(e.Alt&&e.KeyCode==Keys.Down)){Open();e.Handled=true;}
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
            if(popup!=null&&!popup.IsDisposed){popup.Close();return;}
            string[] labels=Items.ConvertAll<string>(delegate(object x){return Convert.ToString(x);}).ToArray();
            popup=new UiPopup(labels,selected,false,Font,delegate(int i){SelectedIndex=i;});
            popup.Open(this, null, 0, false);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent==null?UiTheme.Surface:Parent.BackColor);
            UiTheme.Material(e.Graphics,ClientRectangle,BackColor,false,false,hover,false,Enabled);
            TextRenderer.DrawText(e.Graphics,Text,Font,new Rectangle(12,0,Width-42,Height),Enabled?UiTheme.TextColor:UiTheme.Muted,TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);
            e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
            using(Pen p=new Pen(UiTheme.Muted,1.2f))e.Graphics.DrawLines(p,new Point[] {new Point(Width-23,Height/2-2),new Point(Width-18,Height/2+3),new Point(Width-13,Height/2-2)});
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

    internal sealed class UiPopup : Form
    {
        internal const int Shadow=9, RowHeight=32;
        private readonly string[] labels; private readonly bool menu; private readonly Action<int> choose;
        private readonly int dangerIndex;
        private readonly string status;
        private readonly System.Windows.Forms.Timer animation=new System.Windows.Forms.Timer();
        private int active; private DateTime started; private Rectangle destination; private bool ready;
        private float progress;
        private int dpi = 96;
        private int shadow = Shadow, rowHeight = RowHeight;
        private IntPtr dpiContext;
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
            Font=font; FormBorderStyle=FormBorderStyle.None; ShowInTaskbar=false; TopMost=true; StartPosition=FormStartPosition.Manual;AutoScaleMode=AutoScaleMode.None;
            AccessibleRole=menu?AccessibleRole.MenuPopup:AccessibleRole.List;Text=menu?L.T("设置"):"";
            AccessibleDescription = status;
            animation.Interval=15;animation.Tick+=delegate {progress=Math.Min(1,(float)(DateTime.UtcNow-started).TotalMilliseconds/160);RenderSurface();if(progress>=1)animation.Stop();};
        }
        protected override CreateParams CreateParams {get {CreateParams p=base.CreateParams;p.ExStyle|=0x80000|0x80;return p;}}
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
                bool animate = true;
                SystemParametersInfo(0x1002, 0, out animate, 0);
                progress = animate ? .01f : 1;
                started = DateTime.UtcNow;
                if (owner != null && owner.FindForm() != null) Show(owner.FindForm()); else Show();
                ready = true;
                RenderSurface();
                Activate();
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

        internal Bitmap RenderBitmap()
        {
            Bitmap bitmap=new Bitmap(Width,Height,System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            using(Graphics g=Graphics.FromImage(bitmap))
            {
                g.Clear(Color.Transparent);g.SmoothingMode=SmoothingMode.AntiAlias;g.PixelOffsetMode=PixelOffsetMode.HighQuality;
                Rectangle body = new Rectangle(shadow, shadow, Width - shadow * 2, Height - shadow * 2);
                for (int i = 7; i >= 1; i--)
                {
                    Rectangle outline = body;
                    outline.Inflate(ScalePixel(i), ScalePixel(i));
                    outline.Offset(0, ScalePixel(2));
                    using (GraphicsPath path = UiTheme.Round(outline, ScalePixel(UiTheme.Radius) + i * .6f))
                    using (Brush brush = new SolidBrush(Color.FromArgb(3 + (7 - i), 0, 0, 0)))
                        g.FillPath(brush, path);
                }
                UiTheme.Fill(g, body, UiTheme.Surface, UiTheme.Border);
                int textInset = ScalePixel(menu ? 36 : 12), trailing = ScalePixel(menu ? 10 : 12);
                Color dangerInk = UiTheme.Dark ? Color.FromArgb(252, 154, 158) : Color.FromArgb(161, 35, 52);
                using (Font textFont = new Font(Font.FontFamily, Font.SizeInPoints * dpi / 72f, FontStyle.Regular, GraphicsUnit.Pixel))
                using (var foreground = new SolidBrush(UiTheme.TextColor))
                using (var danger = new SolidBrush(dangerInk))
                using (var format = new StringFormat(StringFormat.GenericDefault))
                {
                    g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                    format.LineAlignment = StringAlignment.Center;
                    format.Trimming = StringTrimming.EllipsisCharacter;
                    format.FormatFlags |= StringFormatFlags.NoWrap;
                    if (StatusHeight > 0)
                        using (var muted = new SolidBrush(UiTheme.Muted))
                            g.DrawString(status, textFont, muted, new Rectangle(body.Left + ScalePixel(12),
                                body.Top + ScalePixel(4), body.Width - ScalePixel(24), StatusHeight), format);
                    for (int i = 0; i < labels.Length; i++)
                    {
                        Rectangle row = RowBounds(i);
                        bool isDanger = i == dangerIndex;
                        if (i == active) UiTheme.Fill(g, row,
                            isDanger ? UiTheme.Blend(UiTheme.Surface, dangerInk, UiTheme.Dark ? 40 : 24) : UiTheme.Input, null);
                        var iconBounds = new Rectangle(row.Left + ScalePixel(10), row.Top + ScalePixel(8), ScalePixel(16), ScalePixel(16));
                        if (isDanger) UiSymbols.Action(g, "exit", iconBounds, dangerInk);
                        else if (menu) UiSymbols.Navigation(g, i == 0 ? "refresh" : "general", iconBounds, UiTheme.Muted);

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
        private void RenderSurface()
        {
            if(!ready || IsDisposed)return;
            AnimationFrames++;
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
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if(e.KeyCode==Keys.Escape){Close();return;}
            if(e.KeyCode==Keys.Enter||e.KeyCode==Keys.Space){Commit();return;}
            if(e.KeyCode==Keys.Up)active=active<0?labels.Length-1:(active+labels.Length-1)%labels.Length;
            if(e.KeyCode==Keys.Down)active=(active+1)%labels.Length;
            if(e.KeyCode==Keys.Home)active=0;if(e.KeyCode==Keys.End)active=labels.Length-1;RenderSurface();e.Handled=true;
        }
        protected override void OnDeactivate(EventArgs e){base.OnDeactivate(e);if(ready)Close();}
        protected override void Dispose(bool disposing){if(disposing)animation.Dispose();base.Dispose(disposing);}
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
    internal sealed class UiTrayMenu : ContextMenuStrip
    {
        private NotifyIcon target; private UiPopup popup;
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
        }
        protected override void Dispose(bool disposing){if(disposing&&popup!=null)popup.Dispose();base.Dispose(disposing);}
    }
}
