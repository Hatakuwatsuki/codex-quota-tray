using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace QuotaTrayApp
{
    // Window-local translations: new binding text must not fall back to Chinese.
    internal static class AccountText
    {
        private static string Pick(string zh, string en, string ja)
        {
            return L.Lang == "en" ? en : (L.Lang == "ja" ? ja : zh);
        }
        internal static string F(string text, params object[] args) { return string.Format(T(text), args); }
        internal static string T(string text)
        {
            switch (text)
            {
                case "账号绑定": return Pick(text, "Account binding", "アカウントの連携");
                case "正在读取本地绑定…": return Pick(text, "Reading local account bindings…", "ローカルの連携情報を読み込み中…");
                case "账号 {0}": return Pick(text, "Account {0}", "アカウント {0}");
                case "未绑定": case "未连接": return Pick(text, "Not bound", "未連携");
                case "使用 Codex 当前账号": return Pick(text, "Use signed-in Codex account", "Codex のログイン中アカウントを使う");
                case "账号读取异常": return Pick(text, "Account check failed", "アカウントの確認に失敗");
                case "更换账号": return Pick(text, "Change account", "アカウントを変更");
                case "通过浏览器连接": return Pick(text, "Connect in browser", "ブラウザーで連携");
                case "通过浏览器绑定": return Pick(text, "Bind in browser", "ブラウザーで連携");
                case "取消绑定": return Pick(text, "Unbind", "連携を解除");
                case "取消绑定这个账号？该位置会空出来，之后可以绑定别的账号。": return Pick(text, "Unbind this account? The slot will be freed for another account.", "このアカウントの連携を解除しますか？この位置は空き、別のアカウントを連携できます。");
                case "已取消绑定。": return Pick(text, "Account unbound.", "連携を解除しました。");
                case "浏览器授权完成后请返回本窗口核对。推荐使用「通过浏览器绑定」；手动指定 Codex 时，切换或关闭登录可能导致读取失败。": return Pick(text, "Return to this window after the browser sign-in completes. “Bind in browser” is recommended; a manually selected Codex may fail to read after the login switches or closes.", "ブラウザーでの認証が終わったら、このウィンドウに戻って確認してください。「ブラウザーで連携」を推奨します。手動で指定した Codex は、ログインの切り替えや終了後に読み取れない場合があります。");
                case "浏览器授权": return Pick(text, "Browser sign-in", "ブラウザー認証");
                case "检查额度": return Pick(text, "Check quota", "利用枠を確認");
                case "确认绑定": return Pick(text, "Confirm binding", "連携を確定");
                case "取消": return Pick(text, "Cancel", "キャンセル");
                case "Codex 查询程序…": return Pick(text, "Codex program…", "Codex プログラム…");
                case "刷新当前账号": return Pick(text, "Refresh current account", "現在のアカウントを更新");
                case "取消操作": return Pick(text, "Cancel action", "操作を中止");
                case "Codex 当前登录": return Pick(text, "Current Codex login", "Codex の現在のログイン");
                case "当前读取：已绑定账号": return Pick(text, "Reading: bound accounts", "読み取り元：連携済みアカウント");
                case "当前读取：Codex 当前登录": return Pick(text, "Reading: current Codex login", "読み取り元：Codex の現在のログイン");
                case "正在取消…": return Pick(text, "Cancelling…", "中止中…");
                case "请在官方网页完成登录，然后回到这里核对账号。": return Pick(text, "Sign in on the official website, then check your account here.", "公式サイトでログインしてから、ここでアカウントを確認してください。");
                case "正在检查…": return Pick(text, "Checking…", "確認中…");
                case "本次未读到额度": return Pick(text, "Quota unavailable", "利用枠を取得できませんでした");
                case "未识别到可绑定账号。请选择 Codex 查询程序，或点击浏览器授权。": return Pick(text, "No account found. Select the Codex program, or use browser sign-in.", "アカウントが見つかりません。Codex プログラムを選択するか、ブラウザーで認証してください。");
                case "请先选择 Codex 查询程序，再识别当前登录或使用浏览器授权。": return Pick(text, "Select the Codex program first, then detect your login or use browser sign-in.", "先に Codex プログラムを選択してから、ログインの確認またはブラウザー認証を行ってください。");
                case "账号列表读取失败，请重新打开窗口。": return Pick(text, "Cannot read accounts. Reopen this window.", "アカウント一覧を読み込めません。ウィンドウを開き直してください。");
                case "选择账号位置的“当前登录”来绑定，或用浏览器授权其他账号。": return Pick(text, "Choose Current login in a slot, or sign in to another account in your browser.", "連携先の「現在のログイン」を選ぶか、ブラウザーで別のアカウントを認証してください。");
                case "绑定这个账号？请核对身份后确认，或取消。": return Pick(text, "Bind this account? Check the identity, then confirm or cancel.", "このアカウントを連携しますか？本人情報を確認し、確定またはキャンセルしてください。");
                case "账号已连接，正在检查额度…": return Pick(text, "Account bound. Checking quota…", "連携しました。利用枠を確認中…");
                case "已核对账号并读到额度。": return Pick(text, "Account verified and quota received.", "アカウントを確認し、利用枠を取得しました。");
                case "选择当前登录，或通过浏览器授权。已有绑定可在这里更换。": return Pick(text, "Use the current login or browser sign-in. You can change existing bindings here.", "現在のログインまたはブラウザー認証を選択してください。既存の連携もここで変更できます。");
                case "待确认": return Pick(text, "Awaiting confirmation", "確認待ち");
                case "已绑定": return Pick(text, "Bound", "連携済み");
                case "操作已取消。": return Pick(text, "Action cancelled.", "操作を中止しました。");
                case "连接超时，请重试。": return Pick(text, "Connection timed out. Try again.", "接続がタイムアウトしました。再試行してください。");
                case "账号身份变化，已停止读取。": return Pick(text, "Account identity changed. Reading stopped.", "アカウントの本人情報が変わったため、読み取りを停止しました。");
                case "这个账号已在另一处连接，请选择另一个。": return Pick(text, "This account is already bound to another slot.", "このアカウントは別の位置に連携済みです。");
                case "另一个连接操作正在进行，请稍后重试。": return Pick(text, "Another account action is running. Try again shortly.", "別の連携操作を実行中です。しばらくしてから再試行してください。");
                case "请先连接此账号。": return Pick(text, "Bind this account first.", "先にこのアカウントを連携してください。");
                case "Windows 凭据库不可用，已停止连接。": return Pick(text, "Windows credential storage is unavailable. Binding stopped.", "Windows 資格情報ストアを利用できないため、連携を停止しました。");
                case "连接失败，请重试。": return Pick(text, "Connection failed. Try again.", "接続に失敗しました。再試行してください。");
                case "{0} 天": return Pick(text, "{0} days", "{0} 日");
                case "{0} 小时": return Pick(text, "{0} hours", "{0} 時間");
                case "{0}剩余 {1}%": return Pick(text, "{0}: {1}% remaining", "{0}：残り {1}%");
                case "暂无额度窗口数据": return Pick(text, "No quota data yet", "利用枠データがありません");
                case "登录地址不可信，已取消。": return Pick(text, "Untrusted sign-in address. Cancelled.", "信頼できないログイン先のため中止しました。");
                case "无法打开官方登录页，请重试。": return Pick(text, "Cannot open the official sign-in page. Try again.", "公式ログインページを開けません。再試行してください。");
                default: return L.T(text);
            }
        }
    }

    internal sealed class ConnectedIdentity
    {
        public string userId;
        public string accountId;
        public string emailMask;
        public string plan;
    }

    internal enum AccountSlotState { Empty, Bound, Pending, Error }

    internal sealed class AccountSlotCard : UiCard
    {
        private AccountSlotState state;

        internal AccountSlotState State
        {
            get { return state; }
            set
            {
                if (value == state) return;
                state = value;
                string description = state == AccountSlotState.Bound ? "已绑定" : state == AccountSlotState.Pending ? "待确认" :
                    state == AccountSlotState.Error ? "账号读取异常" : "未绑定";
                AccessibleDescription = AccountText.T(description);
                Invalidate();
            }
        }

        private Color Indicator
        {
            get
            {
                if (state == AccountSlotState.Error) return UiTheme.Dark ? Color.FromArgb(244, 138, 148) : Color.FromArgb(160, 52, 66);
                if (state == AccountSlotState.Pending) return UiTheme.Dark ? Color.FromArgb(238, 193, 113) : Color.FromArgb(147, 92, 25);
                return UiTheme.AccountColor(AccountSlot);
            }
        }

        internal override Color SurfaceColor
        {
            get { return state == AccountSlotState.Empty || AccountSlot != null ? base.SurfaceColor : UiTheme.Blend(UiTheme.Surface, Indicator, UiTheme.Dark ? 24 : 16); }
        }
        protected override Color BorderColor
        {
            get { return state == AccountSlotState.Empty || AccountSlot != null ? base.BorderColor : UiTheme.Blend(UiTheme.Border, Indicator, 165); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (state == AccountSlotState.Empty) return;
            float s = UiTheme.DpiScaleOf(this);
            int iconSize = UiTheme.S(16, s);
            int x = ClientSize.Width - UiTheme.S(34, s), y = UiTheme.S(20, s);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (Pen pen = new Pen(Indicator, Math.Max(1.3f, 1.5f * s)))
            {
                e.Graphics.DrawEllipse(pen, x, y, iconSize, iconSize);
                if (state == AccountSlotState.Bound)
                    e.Graphics.DrawLines(pen, new[] { new Point(x + UiTheme.S(4, s), y + UiTheme.S(8, s)), new Point(x + UiTheme.S(7, s), y + UiTheme.S(11, s)), new Point(x + UiTheme.S(12, s), y + UiTheme.S(5, s)) });
                else if (state == AccountSlotState.Error)
                {
                    e.Graphics.DrawLine(pen, x + UiTheme.S(8, s), y + UiTheme.S(4, s), x + UiTheme.S(8, s), y + UiTheme.S(9, s));
                    e.Graphics.DrawLine(pen, x + UiTheme.S(8, s), y + UiTheme.S(11, s), x + UiTheme.S(8, s), y + UiTheme.S(13, s));
                }
                else
                {
                    e.Graphics.DrawLine(pen, x + UiTheme.S(8, s), y + UiTheme.S(4, s), x + UiTheme.S(8, s), y + UiTheme.S(8, s));
                    e.Graphics.DrawLine(pen, x + UiTheme.S(8, s), y + UiTheme.S(8, s), x + UiTheme.S(11, s), y + UiTheme.S(9, s));
                }
            }
        }
    }

    internal sealed class ConnectedSlot
    {
        public string slot;
        public bool connected;
        public bool pending;
        public string emailMask;
        public string plan;
        public ConnectedIdentity identity;
        public ConnectedIdentity candidateIdentity;
        public string candidateSource;
        public string source;
    }

    internal sealed class ConnectionReply
    {
        public string @event;
        public bool ok;
        public string slot;
        public string code;
        public string error;
        public string authUrl;
        public string source;
        public ConnectedIdentity identity;
        public ConnectedSlot[] accounts;
        public WindowInfo primary;
        public WindowInfo secondary;
        public ResetCreditsInfo resetCredits;
        public double at;
    }

    // Only non-secret, masked observations from this window; never serialize raw RPC output.
    internal sealed class ConnectionObservation
    {
        public string slot;
        public bool connected;
        public string emailMask;
        public string plan;
        public string code;
        public string checkedAt;
        public WindowInfo primary;
        public WindowInfo secondary;
    }

    internal sealed class AccountConnectionForm : UiWindow
    {
        private static readonly string[] SlotNames = new string[] { "a", "b", "c", "d", "e" };
        private int LayoutGap { get { return S(14); } }
        private int CardInset { get { return S(14); } }
        private int ControlHeight { get { return S(38); } }
        private int LabelHeight { get { return S(24); } }
        private int CardTitleHeight { get { return S(28); } }
        private int GuideLeft { get { return S(28); } }
        private int fittedHeight, lastCardsHeight = -1;
        private bool adjustingLayout;
        private string nodeExe;
        private readonly string script, reportPath;
        private string codexExe;
        internal event EventHandler LocateRequested;
        internal event EventHandler BindingChanged;
        private readonly Label modeLabel;
        private readonly Button locateButton;
        private readonly Button discoverButton;
        private readonly Button[] currentButtons = new Button[5];
        private readonly Panel accountList;
        private readonly Label[] identityLabels = new Label[5];
        private readonly Label[] quotaLabels = new Label[5];
        private readonly Button[] connectButtons = new Button[5];
        private readonly Button[] checkButtons = new Button[5];
        private readonly Button[] unbindButtons = new Button[5];
        internal bool InitialSetup { get; set; }
        private ConnectedIdentity discoveredIdentity;
        private readonly Button[] confirmButtons = new Button[5];
        private readonly Button[] cancelButtons = new Button[5];
        private readonly ConnectedSlot[] slots = new ConnectedSlot[5];
        private readonly ConnectionObservation[] observations = new ConnectionObservation[5];
        private readonly bool[] knownBinding = new bool[5], failedBinding = new bool[5];
        private readonly Label status;
        private readonly Label hint;
        private readonly Button cancelWork;
        private readonly object processGate = new object();
        private Process activeProcess;
        private bool busy, closeWhenIdle;
        private bool backgroundBusy;
        private bool arrangingAccounts, layoutQueued;
        private volatile bool cancellationRequested;
        private bool exiting;

        internal static bool HasBindingStore()
        {
            string local = Environment.GetEnvironmentVariable("LOCALAPPDATA");
            return !String.IsNullOrEmpty(local) && File.Exists(Path.Combine(local, "CodexQuotaTray", "accounts", "accounts.json"));
        }

        internal AccountConnectionForm(string node, string codex, string dataDir, IEnumerable<SettingsAccount> knownAccounts = null)
        {
            nodeExe = node;
            codexExe = codex;
            script = EnsureScript(dataDir);
            reportPath = Path.Combine(dataDir, "account-status.json");
            if (knownAccounts != null)
                foreach (SettingsAccount account in knownAccounts)
                {
                    int index = Array.IndexOf(SlotNames, account.Slot);
                    if (index < 0) continue;
                    knownBinding[index] = true;
                    failedBinding[index] = account.Failed;
                }
            Text = AccountText.T("账号绑定");
            Font = UiTheme.UiFont(10.5f);
            MaximizeBox = false;
            MinimizeBox = false;
            ShowIcon = true;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(S(760), S(600));
            int availableHeight = Screen.FromPoint(Cursor.Position).WorkingArea.Height;
            if (Height > availableHeight) Height = availableHeight;
            MinimumSize = new Size(S(760), Math.Min(S(500), availableHeight));
            modeLabel = new UiLabel();
            UpdateReadingMode();
            modeLabel.SetBounds(GuideLeft, ScaledCaptionHeight + S(12), Math.Max(1, ClientSize.Width - GuideLeft * 2), S(30));
            modeLabel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            Controls.Add(modeLabel);
            accountList = new UiPanel();
            accountList.AutoScroll = true;
            accountList.SetBounds(S(16), ScaledCaptionHeight + S(52), Math.Max(1, ClientSize.Width - S(32)), ClientSize.Height - ScaledCaptionHeight - S(216));
            accountList.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            Controls.Add(accountList);
            for (int i = 0; i < SlotNames.Length; i++)
            {
                int index = i;
                string slot = SlotNames[i];
                slots[i] = new ConnectedSlot { slot = slot };
                observations[i] = new ConnectionObservation { slot = slot, code = "not_connected" };
                GroupBox box = new AccountSlotCard();
                box.Text = AccountText.F("账号 {0}", (i + 1).ToString());
                box.SetBounds(S(12), i * (ControlHeight + LayoutGap), Math.Max(1, accountList.ClientSize.Width - S(24)), ControlHeight);
                box.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
                accountList.Controls.Add(box);
                Label who = new UiLabel();
                who.AutoEllipsis = true;
                who.SetBounds(CardInset, CardInset + CardTitleHeight, Math.Max(1, box.ClientSize.Width - CardInset * 2), LabelHeight);
                box.Controls.Add(who);
                identityLabels[i] = who;
                Label quota = new UiLabel();
                quota.SetBounds(CardInset, CardInset + CardTitleHeight + LabelHeight + LayoutGap, Math.Max(1, box.ClientSize.Width - CardInset * 2), LabelHeight);
                quota.Visible = false;
                box.Controls.Add(quota);
                quotaLabels[i] = quota;
                currentButtons[i] = AddButton(box, AccountText.T("使用 Codex 当前账号"), CardInset, delegate { Run("bind-current", index); });
                ((UiButton)currentButtons[i]).Accent = true;
                connectButtons[i] = AddButton(box, AccountText.T("通过浏览器绑定"), CardInset, delegate { Run("login", index); });
                checkButtons[i] = AddButton(box, AccountText.T("检查额度"), CardInset, delegate { Run("query", index); });
                unbindButtons[i] = AddButton(box, AccountText.T("取消绑定"), CardInset, delegate { ConfirmUnbind(index); });
                ((UiButton)unbindButtons[i]).Danger = true;
                unbindButtons[i].SetBounds(CardInset, 0, 1, ControlHeight);
                confirmButtons[i] = AddButton(box, AccountText.T("确认绑定"), CardInset, delegate { Run("confirm", index); });
                cancelButtons[i] = AddButton(box, AccountText.T("取消"), CardInset, delegate { Run("cancel", index); });
                connectButtons[i].SetBounds(CardInset, 0, 1, ControlHeight); ((UiButton)connectButtons[i]).Accent = false;
                checkButtons[i].SetBounds(CardInset, 0, 1, ControlHeight);
                confirmButtons[i].SetBounds(CardInset, 0, 1, ControlHeight); ((UiButton)confirmButtons[i]).Accent = true;
                cancelButtons[i].SetBounds(CardInset, 0, 1, ControlHeight);
                who.Anchor = quota.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
                checkButtons[i].Anchor = AnchorStyles.Top | AnchorStyles.Left;
            }
            locateButton = new UiButton();
            locateButton.Text = AccountText.T("Codex 查询程序…");
            locateButton.SetBounds(GuideLeft, 0, 1, ControlHeight);
            locateButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            locateButton.Click += delegate { if (!busy && LocateRequested != null) LocateRequested(this, EventArgs.Empty); };
            Controls.Add(locateButton);
            discoverButton = new UiButton();
            discoverButton.Text = AccountText.T("刷新当前账号");
            discoverButton.SetBounds(GuideLeft, 0, 1, ControlHeight);
            discoverButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            discoverButton.Click += delegate { if (!busy) Run("discover", -1); };
            Controls.Add(discoverButton);
            status = new UiLabel();
            status.SetBounds(GuideLeft, 0, 1, 1);
            status.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            Controls.Add(status);
            hint = new UiLabel();
            hint.UseMnemonic = false;
            hint.ForeColor = UiTheme.Muted;
            hint.Text = AccountText.T("浏览器授权完成后请返回本窗口核对。推荐使用「通过浏览器绑定」；手动指定 Codex 时，切换或关闭登录可能导致读取失败。");
            hint.SetBounds(GuideLeft, 0, 1, 1);
            hint.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            Controls.Add(hint);
            cancelWork = new UiButton();
            cancelWork.Text = AccountText.T("取消操作");
            cancelWork.SetBounds(GuideLeft, 0, 1, ControlHeight);
            cancelWork.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            ((UiButton)cancelWork).Danger = true;
            cancelWork.Click += delegate { RequestCancellation(); };
            Controls.Add(cancelWork);
            SizeChanged += delegate { if (!adjustingLayout) QueueLayout(); };
            Load += delegate {
                ActiveControl = locateButton;
                accountList.AutoScrollPosition = Point.Empty;
                fittedHeight = 0;
                UpdateButtons();
            };
            Shown += delegate {
                if (HasBindingStore()) BeginInvoke((MethodInvoker)delegate { Run("list", -1); });
            };
            FormClosing += delegate(object sender, FormClosingEventArgs e)
            {
                if (!busy) return;
                e.Cancel = true;
                closeWhenIdle = true;
                RequestCancellation();
            };
            UpdateButtons();
            UiTheme.Hook(this);
        }

        private Button AddButton(Control parent, string text, int x, EventHandler click)
        {
            Button b = new UiButton();
            b.Text = text;
            b.SetBounds(x, 0, 110, ControlHeight);
            b.AutoSize = false;
            b.Click += click;
            parent.Controls.Add(b);
            return b;
        }

        private void ConfirmUnbind(int index)
        {
            if (busy || index < 0 || index >= slots.Length || slots[index] == null || !slots[index].connected) return;
            string message = AccountText.T("取消绑定这个账号？该位置会空出来，之后可以绑定别的账号。");
            if (MessageBox.Show(this, message, Text, MessageBoxButtons.OKCancel, MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2) != DialogResult.OK) return;
            Run("unbind", index);
        }

        private void QueueLayout()
        {
            if (arrangingAccounts || layoutQueued || !IsHandleCreated) return;
            layoutQueued = true;
            // WinForms may still be scaling children when SizeChanged fires.
            BeginInvoke((MethodInvoker)delegate
            {
                layoutQueued = false;
                if (!IsDisposed) UpdateButtons();
            });
        }

        private void UpdateButtons()
        {
            if (arrangingAccounts) return;
            arrangingAccounts = true;
            try { ArrangeAccounts(); }
            finally { arrangingAccounts = false; }
        }

        private static void SetBoundsIfChanged(Control control, int x, int y, int width, int height)
        {
            if (control.Left != x || control.Top != y || control.Width != width || control.Height != height)
                control.SetBounds(x, y, width, height);
        }
        private static void SetTextIfChanged(Control control, string text)
        {
            if (control.Text != text) control.Text = text;
        }
        private static void SetVisibleIfChanged(Control control, bool visible)
        {
            if (control.Visible != visible) control.Visible = visible;
        }
        private void ArrangeAccounts()
        {
            adjustingLayout = true;
            SuspendLayout();
            accountList.SuspendLayout();
            try
            {
            int scrollY = accountList.AutoScrollPosition.Y;
            int y = 0;
            int currentSlot = CurrentAccountTarget();
            int cardGap = S(4), cardTail = S(8);
            for (int i = 0; i < SlotNames.Length; i++)
            {
                ConnectedSlot slot = slots[i];
                bool pending = slot.pending;
                bool showCurrent = !pending && i == currentSlot;
                bool hasIdentity = !String.IsNullOrEmpty(identityLabels[i].Text);
                bool hasQuota = !pending && !String.IsNullOrEmpty(quotaLabels[i].Text);
                AccountSlotCard card = (AccountSlotCard)connectButtons[i].Parent;
                card.AccountSlot = slot.connected ? slot.slot : null;
                card.State = pending ? AccountSlotState.Pending :
                    failedBinding[i] || (!slot.connected && knownBinding[i]) ? AccountSlotState.Error :
                    slot.connected ? AccountSlotState.Bound : AccountSlotState.Empty;

                SetVisibleIfChanged(identityLabels[i], hasIdentity);
                int quotaTop = CardInset + CardTitleHeight + (hasIdentity ? LabelHeight : 0) + cardGap;
                if (quotaLabels[i].Top != quotaTop) quotaLabels[i].Top = quotaTop;
                SetVisibleIfChanged(quotaLabels[i], hasQuota);
                int buttonY = CardInset + CardTitleHeight + (hasIdentity ? LabelHeight : 0) + (hasQuota ? LabelHeight + cardGap : 0) + cardTail;
                int cardH = (card.State == AccountSlotState.Empty && !showCurrent)
                    ? (CardInset * 2 + ControlHeight)
                    : (buttonY + ControlHeight + CardInset);
                if (card.State == AccountSlotState.Empty && !showCurrent) buttonY = CardInset;
                SetBoundsIfChanged(card, CardInset, scrollY + y, card.Width, cardH);
                y += cardH + LayoutGap;

                SetTextIfChanged(connectButtons[i], AccountText.T(slot.connected ? "更换账号" : "通过浏览器绑定"));
                bool available = !busy || backgroundBusy;
                SetVisibleIfChanged(currentButtons[i], showCurrent);
                currentButtons[i].Enabled = showCurrent && !busy;
                SetVisibleIfChanged(connectButtons[i], !pending);
                connectButtons[i].Enabled = available && !pending;
                SetVisibleIfChanged(checkButtons[i], slot.connected && !pending);
                checkButtons[i].Enabled = available && slot.connected && !pending;
                SetVisibleIfChanged(unbindButtons[i], slot.connected && !pending);
                unbindButtons[i].Enabled = available && slot.connected && !pending;
                SetVisibleIfChanged(confirmButtons[i], pending);
                SetVisibleIfChanged(cancelButtons[i], pending);
                confirmButtons[i].Enabled = cancelButtons[i].Enabled = !busy && pending;

                int left = CardInset;
                if (showCurrent) left = PlaceAction(currentButtons[i], left, buttonY);
                if (pending)
                {
                    left = PlaceAction(confirmButtons[i], left, buttonY);
                    PlaceAction(cancelButtons[i], left, buttonY);
                }
                else
                {
                    if (slot.connected) left = PlaceAction(checkButtons[i], left, buttonY);
                    left = PlaceAction(connectButtons[i], left, buttonY);
                    // 取消绑定单独靠右，和左侧“检查额度／更换账号”分开。
                    if (slot.connected) RightAlignAction(unbindButtons[i], card, buttonY);
                }
            }
            SetVisibleIfChanged(cancelWork, busy && !backgroundBusy);
            cancelWork.Enabled = !cancellationRequested;
            discoverButton.Enabled = !busy;
            locateButton.Enabled = !busy || backgroundBusy;
            LayoutFooter(y - LayoutGap);
            // 卡片左右各留 CardInset；出现竖向滚动条时把滚动条宽度扣掉，
            // 否则右侧会被滚动条吞掉一截，看起来左右不等宽。
            int scrollbar = accountList.VerticalScroll.Visible ? SystemInformation.VerticalScrollBarWidth : 0;
            int listWidth = Math.Max(1, accountList.ClientSize.Width - 2 * CardInset - scrollbar);
            for (int i = 0; i < SlotNames.Length; i++)
            {
                AccountSlotCard card = (AccountSlotCard)connectButtons[i].Parent;
                SetBoundsIfChanged(card, CardInset, card.Top, listWidth, card.Height);
                int labelWidth = Math.Max(1, card.ClientSize.Width - 2 * CardInset);
                SetBoundsIfChanged(identityLabels[i], CardInset, CardInset + CardTitleHeight, labelWidth, LabelHeight);
                SetBoundsIfChanged(quotaLabels[i], CardInset, quotaLabels[i].Top, labelWidth, LabelHeight);
                if (card.State == AccountSlotState.Empty && i != currentSlot)
                {
                    int emptyLeft = card.ClientSize.Width - CardInset - ActionWidth(connectButtons[i].Text, connectButtons[i].Font, UiScale);
                    if (connectButtons[i].Left != emptyLeft) connectButtons[i].Left = emptyLeft;
                }
                // 用状态判断而不是 Visible：窗口尚未显示时 Visible 恒为 false，
                // 会漏掉这次真正对齐的机会。
                if (slots[i].connected && !slots[i].pending) RightAlignAction(unbindButtons[i], card, unbindButtons[i].Top);
            }
            }
            finally
            {
                accountList.ResumeLayout(false);
                ResumeLayout(false);
                adjustingLayout = false;
            }
        }

        private int PlaceAction(Button button, int left, int top)
        {
            // Content width, matching the 1.8.19 layout rule. A shared maximum width
            // overflowed the card once a fourth action (取消绑定) joined the row.
            int width = ActionWidth(button.Text, button.Font, UiScale);
            SetBoundsIfChanged(button, left, top, width, ControlHeight);
            return left + width + LayoutGap;
        }

        private void RightAlignAction(Button button, Control card, int top)
        {
            int width = ActionWidth(button.Text, button.Font, UiScale);
            SetBoundsIfChanged(button, Math.Max(CardInset, card.ClientSize.Width - CardInset - width), top, width, ControlHeight);
        }

        private int CurrentAccountTarget()
        {
            if (discoveredIdentity == null || String.IsNullOrEmpty(discoveredIdentity.userId)) return -1;
            int empty = -1, previous = -1;
            for (int i = 0; i < slots.Length; i++)
            {
                ConnectedSlot slot = slots[i];
                if (slot.pending || (slot.connected && slot.identity != null &&
                    String.Equals(slot.identity.userId, discoveredIdentity.userId, StringComparison.Ordinal))) return -1;
                if (empty < 0 && !slot.connected) empty = i;
                if (previous < 0 && slot.connected && slot.source == "current" && failedBinding[i]) previous = i;
            }
            return previous >= 0 ? previous : empty;
        }

        private void LayoutFooter(int cardsHeight)
        {
            int listTop = ScaledCaptionHeight + S(52);
            int rowWidth = Math.Max(1, ClientSize.Width - 2 * GuideLeft);
            SetVisibleIfChanged(status, !String.IsNullOrWhiteSpace(status.Text));
            int statusHeight = String.IsNullOrWhiteSpace(status.Text) ? 0 :
                TextRenderer.MeasureText(status.Text, status.Font, new Size(rowWidth, Int32.MaxValue),
                    TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix).Height;
            int hintHeight = TextRenderer.MeasureText(hint.Text, hint.Font, new Size(rowWidth, Int32.MaxValue),
                TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix).Height;
            // 底部留出与左右一致的边距：提示文字不再贴着窗口下沿。
            int footerHeight = ControlHeight + LayoutGap + (statusHeight == 0 ? 0 : LayoutGap + statusHeight) + LayoutGap + hintHeight + GuideLeft;
            if ((!Visible || !busy || cardsHeight != lastCardsHeight) && (fittedHeight == 0 || fittedHeight == ClientSize.Height))
            {
                Rectangle work = (IsHandleCreated ? Screen.FromHandle(Handle) : Screen.FromPoint(Cursor.Position)).WorkingArea;
                int desired = listTop + cardsHeight + LayoutGap + footerHeight;
                int targetHeight = Math.Min(work.Height, Math.Max(MinimumSize.Height, desired));
                if (ClientSize.Height != targetHeight)
                {
                    // UiWindow handles WM_NCCALCSIZE itself, so the live window is all client area.
                    // Form.ClientSize's setter still adds the standard resize frame to both dimensions.
                    if (IsHandleCreated) Height = targetHeight;
                    else ClientSize = new Size(ClientSize.Width, targetHeight);
                    if (Visible && Bottom > work.Bottom) Top = Math.Max(work.Top, work.Bottom - Height);
                }
                fittedHeight = ClientSize.Height;
                lastCardsHeight = cardsHeight;
            }

            int firstY = ClientSize.Height - footerHeight;
            int width = Math.Max(ActionWidth(locateButton.Text, locateButton.Font, UiScale),
                Math.Max(ActionWidth(discoverButton.Text, discoverButton.Font, UiScale), ActionWidth(cancelWork.Text, cancelWork.Font, UiScale)));
            SetBoundsIfChanged(locateButton, GuideLeft, firstY, width, ControlHeight);
            SetBoundsIfChanged(discoverButton, GuideLeft + width + LayoutGap, firstY, width, ControlHeight);
            SetBoundsIfChanged(cancelWork, GuideLeft + 2 * (width + LayoutGap), firstY, width, ControlHeight);
            SetBoundsIfChanged(status, GuideLeft, firstY + ControlHeight + LayoutGap, rowWidth, statusHeight);
            SetBoundsIfChanged(hint, GuideLeft, status.Bottom + LayoutGap, rowWidth, hintHeight);
            SetBoundsIfChanged(modeLabel, GuideLeft, ScaledCaptionHeight + S(12), rowWidth, S(30));
            SetBoundsIfChanged(accountList, S(16), listTop, Math.Max(1, ClientSize.Width - S(32)), Math.Max(S(80), firstY - LayoutGap - listTop));
        }

        private static int ActionWidth(string label, Font font, float scale = 1f)
        {
            int text = TextRenderer.MeasureText(label, font, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width;
            return Math.Max(UiTheme.S(120, scale), text + UiTheme.S(24, scale));
        }

        internal void SetCodexPath(string path, string node)
        {
            if (busy) throw new InvalidOperationException("Account operation in progress");
            codexExe = path;
            nodeExe = node;
            discoveredIdentity = null;
            status.Text = "";
            UpdateReadingMode();
            if (HasBindingStore()) Run("list", -1); else UpdateButtons();
        }

        private void UpdateReadingMode()
        {
            bool connected = false;
            foreach (ConnectedSlot slot in slots) if (slot != null && slot.connected) connected = true;
            bool codexMissing = String.IsNullOrEmpty(codexExe) || !File.Exists(codexExe);
            modeLabel.Name = codexMissing ? "account-status-warning" : "account-binding-mode";
            modeLabel.ForeColor = codexMissing ? UiTheme.WarningText : UiTheme.TextColor;
            if (codexMissing)
            {
                modeLabel.Text = L.T("未识别到 Codex") + "  ·  " +
                    (connected ? AccountText.T("当前读取：已绑定账号") : L.T("未绑定任何账号"));
                return;
            }
            modeLabel.Text = discoveredIdentity != null
                ? AccountText.T("Codex 当前登录") + "  ·  " + SafeEmailMask(discoveredIdentity.emailMask) + "  ·  " + PlanNames.Title(discoveredIdentity.plan)
                : connected ? AccountText.T("当前读取：已绑定账号") : L.T("未绑定任何账号");
        }

        private void Post(Action action)
        {
            if (IsDisposed || !IsHandleCreated) return;
            try { BeginInvoke(action); } catch (InvalidOperationException) { }
        }

        private void RequestCancellation()
        {
            cancellationRequested = true;
            status.Text = AccountText.T("正在取消…");
            UpdateButtons();
            lock (processGate)
            {
                if (activeProcess != null)
                {
                    try { activeProcess.StandardInput.WriteLine("{\"action\":\"cancel\"}"); activeProcess.StandardInput.Flush(); } catch { }
                }
            }
        }

        internal void StopForExit()
        {
            lock (processGate) exiting = true;
            RequestCancellation();
            lock (processGate)
            {
                if (activeProcess == null) return;
                try
                {
                    if (activeProcess.WaitForExit(3000)) return;
                    var stop = new ProcessStartInfo("taskkill.exe", "/PID " + activeProcess.Id + " /T /F");
                    stop.UseShellExecute = false;
                    stop.CreateNoWindow = true;
                    using (Process killer = Process.Start(stop)) killer.WaitForExit(5000);
                }
                catch { }
            }
        }

        private void Run(string command, int index)
        {
            if (busy) return;
            if (command != "list" && command != "unbind" && (String.IsNullOrEmpty(codexExe) || !File.Exists(codexExe)))
            {
                status.Text = ConnectionError("missing_environment");
                UpdateButtons();
                return;
            }
            busy = true;
            backgroundBusy = command == "list" || command == "discover";
            cancellationRequested = false;
            if (command != "list")
                SetTextIfChanged(status, AccountText.T(command == "login" ? "请在官方网页完成登录，然后回到这里核对账号。" : "正在检查…"));
            UpdateButtons();
            string slot = index < 0 ? "" : SlotNames[index];
            ConnectedIdentity expected = index < 0 ? null :
                command == "confirm" ? slots[index].candidateIdentity : slots[index].identity;
            ThreadPool.QueueUserWorkItem(delegate
            {
                ConnectionReply result = Execute(command, slot);
                Post(delegate
                {
                    busy = false;
                    backgroundBusy = false;
                    if (closeWhenIdle) { Close(); return; }
                    if (cancellationRequested && command != "list")
                    {
                        if (index >= 0)
                        {
                            quotaLabels[index].Text = "";
                            observations[index].checkedAt = null;
                            observations[index].primary = null;
                            observations[index].secondary = null;
                        }
                        Run("list", -1); // Cancellation may race an already committed confirmation; reload persisted state.
                        return;
                    }
                    if (cancellationRequested) result = Failure("cancelled");
                    if (result != null && result.ok && (command == "confirm" || command == "query") &&
                        (expected == null || result.identity == null || expected.userId != result.identity.userId || expected.accountId != result.identity.accountId))
                        result = Failure("identity_mismatch");
                    if (result != null && result.ok && command == "query" && !ValidWindow(result.primary) && !ValidWindow(result.secondary))
                        result = Failure("no_quota_data");
                    if (result == null || !result.ok)
                    {
                        if (index >= 0)
                        {
                            observations[index].code = result == null ? "client_error" : SafeCode(result.code);
                            observations[index].checkedAt = null;
                            observations[index].primary = null;
                            observations[index].secondary = null;
                            if (command == "query")
                            {
                                failedBinding[index] = true;
                                quotaLabels[index].Text = "";
                            }
                            SaveObservations();
                        }
                        string failure = result == null ? "client_error" : result.code;
                        status.Text = (command == "discover" || command == "bind-current") &&
                            (failure == "identity_unavailable" || failure == "auth_failed")
                            ? AccountText.T("未识别到可绑定账号。请选择 Codex 查询程序，或点击浏览器授权。")
                            : ConnectionError(failure);
                        if (command == "discover") { discoveredIdentity = null; UpdateReadingMode(); }
                        if (command == "list")
                        {
                            for (int i = 0; i < slots.Length; i++) failedBinding[i] = knownBinding[i];
                            modeLabel.Text = AccountText.T("账号列表读取失败，请重新打开窗口。");
                        }
                        UpdateButtons();
                        return;
                    }
                    if (command == "list")
                    {
                        ApplyList(result.accounts);
                    }
                    else if (command == "discover")
                    {
                        discoveredIdentity = result.identity;
                        UpdateReadingMode();
                        status.Text = "";
                        bool anyBound = false;
                        foreach (ConnectedSlot item in slots) if (item.connected || item.pending) anyBound = true;
                        if (InitialSetup && !anyBound)
                        {
                            Run("bind-current", 0);
                            return;
                        }
                    }
                    else if (result.@event == "candidate")
                    {
                        slots[index].pending = true;
                        slots[index].candidateSource = result.source;
                        slots[index].candidateIdentity = result.identity;
                        SetIdentity(index, result.identity, true);
                        status.Text = AccountText.T("绑定这个账号？请核对身份后确认，或取消。");
                    }
                    else if (command == "confirm")
                    {
                        slots[index].pending = false;
                        slots[index].connected = true;
                        slots[index].source = slots[index].candidateSource;
                        slots[index].candidateSource = null;
                        slots[index].candidateIdentity = null;
                        knownBinding[index] = true;
                        failedBinding[index] = false;
                        if (BindingChanged != null) BindingChanged(this, EventArgs.Empty);
                        UpdateReadingMode();
                        slots[index].identity = result.identity;
                        SetIdentity(index, result.identity, false);
                        status.Text = AccountText.T("已绑定");
                    }
                    else if (command == "cancel")
                    {
                        Run("list", -1); // Reload the preserved binding instead of clearing it locally.
                        return;
                    }
                    else if (command == "unbind")
                    {
                        slots[index].connected = false;
                        slots[index].pending = false;
                        slots[index].identity = null;
                        slots[index].source = null;
                        slots[index].candidateIdentity = null;
                        slots[index].candidateSource = null;
                        knownBinding[index] = false;
                        failedBinding[index] = false;
                        identityLabels[index].Text = "";
                        quotaLabels[index].Text = "";
                        observations[index] = new ConnectionObservation { slot = SlotNames[index], code = "not_connected" };
                        status.Text = AccountText.T("已取消绑定。");
                        if (BindingChanged != null) BindingChanged(this, EventArgs.Empty);
                        Run("list", -1);
                        return;
                    }
                    else if (command == "query")
                    {
                        SetIdentity(index, result.identity, false);
                        quotaLabels[index].Text = QuotaText(result.primary, result.secondary);
                        failedBinding[index] = false;
                        observations[index].code = "ok";
                        observations[index].checkedAt = DateTime.UtcNow.ToString("o");
                        observations[index].primary = result.primary;
                        observations[index].secondary = result.secondary;
                        SaveObservations();
                        status.Text = AccountText.T("已核对账号并读到额度。");
                    }
                    UpdateButtons();
                });
            });
        }

        private void ApplyList(ConnectedSlot[] accounts)
        {
            adjustingLayout = true;
            SuspendLayout();
            accountList.SuspendLayout();
            try
            {
            for (int i = 0; i < SlotNames.Length; i++)
            {
                string slot = SlotNames[i];
                slots[i] = new ConnectedSlot { slot = slot };
                observations[i] = new ConnectionObservation { slot = slot, code = "not_checked" };
                SetTextIfChanged(identityLabels[i], "");
                SetTextIfChanged(quotaLabels[i], "");
            }
            if (accounts != null)
                foreach (ConnectedSlot item in accounts)
                {
                    if (item == null) continue;
                    int i = Array.IndexOf(SlotNames, item.slot);
                    if (i < 0) continue;
                    slots[i] = item;
                    if (item.connected) knownBinding[i] = true;
                    if (item.pending && !item.connected && item.candidateIdentity == null)
                    {
                        item.candidateIdentity = item.identity;
                        item.candidateSource = item.source;
                    }
                    ConnectedIdentity identity = (item.pending ? item.candidateIdentity : item.identity)
                        ?? new ConnectedIdentity { emailMask = item.emailMask, plan = item.plan };
                    if (item.connected || item.pending) SetIdentity(i, identity, item.pending);
                }
            UpdateReadingMode();
            ActiveControl = locateButton;
            accountList.AutoScrollPosition = Point.Empty;
            SetTextIfChanged(status, "");
            SaveObservations();
            }
            finally
            {
                accountList.ResumeLayout(false);
                ResumeLayout(false);
                adjustingLayout = false;
            }
        }

        private void SetIdentity(int index, ConnectedIdentity identity, bool pending)
        {
            if (identity == null) return;
            string mask = SafeEmailMask(identity.emailMask);
            string plan = SafePlan(identity.plan);
            string source = pending ? slots[index].candidateSource : slots[index].source;
            identityLabels[index].Text = mask + "  ·  " + PlanNames.Title(plan)
                + (source == "current" ? "  ·  " + AccountText.T("Codex 当前登录") : "");
            observations[index].connected = slots[index].connected;
            observations[index].emailMask = mask;
            observations[index].plan = plan;
        }

        private static string SafeEmailMask(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length > 128) return "***";
            return System.Text.RegularExpressions.Regex.IsMatch(value, @"^[A-Za-z0-9._%+\-]{0,2}\*{3,}@[A-Za-z0-9.\-]{1,100}$") ? value : "***";
        }

        private static string SafePlan(string value)
        {
            return PlanNames.Store(value);
        }

        private static string SafeCode(string value)
        {
            switch (value)
            {
                case "cancelled": case "timeout": case "identity_mismatch": case "duplicate_account":
                case "busy": case "account_busy": case "store_busy": case "not_connected": case "keyring_unavailable":
                case "invalid_argument": case "missing_environment": case "unsafe_path": case "already_connected":
                case "no_candidate": case "identity_unavailable": case "auth_failed": case "invalid_url":
                case "server_error": case "invalid_response": case "storage_error": case "no_quota_data":
                case "node_start_failed": case "codex_launch_failed":
                    return value;
                default: return "client_error";
            }
        }

        internal static string ConnectionError(string code)
        {
            switch (code)
            {
                case "missing_environment": return AccountText.T("请先选择 Codex 查询程序，再识别当前登录或使用浏览器授权。");
                case "node_start_failed": return L.T("连接运行时无法启动。请重新选择 Codex 查询程序，或安装 Node.js 后重试。");
                case "codex_launch_failed": return L.T("无法启动所选的 Codex 查询程序，请重新选择。");
                case "server_error": return L.T("Codex 查询服务启动或握手失败。请检查 Codex 是否能正常运行。");
                case "cancelled": return AccountText.T("操作已取消。");
                case "timeout": return AccountText.T("连接超时，请重试。");
                case "identity_mismatch": return AccountText.T("账号身份变化，已停止读取。");
                case "duplicate_account": return AccountText.T("这个账号已在另一处连接，请选择另一个。");
                case "busy": case "account_busy": case "store_busy": return AccountText.T("另一个连接操作正在进行，请稍后重试。");
                case "not_connected": return AccountText.T("请先连接此账号。");
                case "auth_failed": return AccountText.T("授权回调已收到，但 token 交换连接失败；请检查网络或代理后重试。");
                case "keyring_unavailable": return AccountText.T("Windows 凭据库不可用，已停止连接。");
                default: return AccountText.T("连接失败，请重试。");
            }
        }

        private static bool ValidWindow(WindowInfo w)
        {
            return w != null && !double.IsNaN(w.used) && !double.IsInfinity(w.used) && w.used >= 0 && w.used <= 100 &&
                !double.IsNaN(w.windowMins) && !double.IsInfinity(w.windowMins) && w.windowMins > 0;
        }

        private static string QuotaText(WindowInfo primary, WindowInfo secondary)
        {
            string text = "";
            foreach (WindowInfo w in new WindowInfo[] { primary, secondary })
            {
                if (w == null || w.used < 0 || w.used > 100 || w.windowMins <= 0) continue;
                string window = QuotaWindow.IsLong(w.windowMins) ? AccountText.F("{0} 天", QuotaWindow.Days(w.windowMins)) : AccountText.F("{0} 小时", Math.Round(w.windowMins / 60, 1));
                if (text.Length > 0) text += "   ";
                text += AccountText.F("{0}剩余 {1}%", window, 100 - (int)Math.Round(w.used));
            }
            return text.Length == 0 ? AccountText.T("暂无额度窗口数据") : text;
        }

        private void SaveObservations()
        {
            try
            {
                string json = new JavaScriptSerializer().Serialize(new { at = DateTime.UtcNow.ToString("o"), accounts = observations });
                File.WriteAllText(reportPath, json, new UTF8Encoding(false));
            }
            catch { /* Diagnostic failure must never disclose RPC output or interrupt authentication. */ }
        }

        private ConnectionReply Execute(string command, string slot)
        {
            lock (AccountOperationGate.Sync) return ExecuteCore(command, slot);
        }

        private ConnectionReply ExecuteCore(string command, string slot)
        {
            ConnectionReply result = null;
            object resultGate = new object();
            bool badOutput = false;
            try
            {
                // Listing is local metadata only; missing Codex must not hide the five slots.
                if (command != "list" && command != "unbind" && (string.IsNullOrEmpty(codexExe) || !File.Exists(codexExe)))
                    return Failure("missing_environment");
                var psi = new ProcessStartInfo(nodeExe, "\"" + script + "\" " + command + (slot.Length == 0 ? "" : " " + slot));
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardInput = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.StandardOutputEncoding = Encoding.UTF8;
                psi.StandardErrorEncoding = Encoding.UTF8;
                if (!string.IsNullOrEmpty(codexExe)) psi.EnvironmentVariables["CODEX_CLI_PATH"] = codexExe;
                else psi.EnvironmentVariables.Remove("CODEX_CLI_PATH");
                using (var p = new Process())
                {
                    p.StartInfo = psi;
                    p.ErrorDataReceived += delegate { };
                    p.OutputDataReceived += delegate(object sender, DataReceivedEventArgs e)
                    {
                        if (e.Data == null) return;
                        if (e.Data.Length > 32768) { lock (resultGate) badOutput = true; return; }
                        try
                        {
                            ConnectionReply msg = new JavaScriptSerializer().Deserialize<ConnectionReply>(e.Data);
                            if (msg == null) return;
                            if (command != "list" && command != "discover" && msg.slot != slot)
                            {
                                lock (resultGate) badOutput = true;
                                return;
                            }
                            if (msg.@event == "candidate" && command != "login" && command != "bind-current")
                            {
                                lock (resultGate) badOutput = true;
                                return;
                            }
                            if (msg.ok && (command == "confirm" || command == "query" || command == "discover" || msg.@event == "candidate") &&
                                (msg.identity == null || string.IsNullOrEmpty(msg.identity.userId) || string.IsNullOrEmpty(msg.identity.accountId)))
                            {
                                lock (resultGate) badOutput = true;
                                return;
                            }
                            if (msg.@event == "login")
                            {
                                string url = msg.authUrl;
                                Post(delegate
                                {
                                    if (cancellationRequested || closeWhenIdle) return;
                                    Uri uri;
                                    if (!Uri.TryCreate(url, UriKind.Absolute, out uri) || uri.Scheme != "https" || !uri.IsDefaultPort || uri.UserInfo.Length != 0 || (uri.DnsSafeHost != "auth.openai.com" && uri.DnsSafeHost != "chatgpt.com"))
                                    {
                                        status.Text = AccountText.T("登录地址不可信，已取消。");
                                        RequestCancellation();
                                        return;
                                    }
                                    try { Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }); }
                                    catch { status.Text = AccountText.T("无法打开官方登录页，请重试。"); RequestCancellation(); }
                                });
                            }
                            else if (msg.@event == "result" || msg.@event == "candidate")
                            {
                                lock (resultGate) result = msg;
                            }
                        }
                        catch { lock (resultGate) badOutput = true; }
                    };
                    lock (processGate)
                    {
                        if (exiting || cancellationRequested) return Failure("cancelled");
                        try { p.Start(); }
                        catch (System.ComponentModel.Win32Exception) { return Failure("node_start_failed"); }
                        catch (InvalidOperationException) { return Failure("node_start_failed"); }
                        activeProcess = p;
                    }
                    p.BeginOutputReadLine();
                    p.BeginErrorReadLine();
                    Stopwatch clock = Stopwatch.StartNew();
                    long cancelAt = -1;
                    int timeout = command == "login" ? 240000 : 90000;
                    while (!p.WaitForExit(200))
                    {
                        if (cancellationRequested && cancelAt < 0) cancelAt = clock.ElapsedMilliseconds;
                        if (clock.ElapsedMilliseconds < timeout && (cancelAt < 0 || clock.ElapsedMilliseconds - cancelAt < 12000)) continue;
                        // Kill only the process tree created by this operation; never a global Codex process.
                        try
                        {
                            var stop = new ProcessStartInfo("taskkill.exe", "/PID " + p.Id + " /T /F");
                            stop.UseShellExecute = false;
                            stop.CreateNoWindow = true;
                            using (Process killer = Process.Start(stop)) killer.WaitForExit(5000);
                        }
                        catch { try { p.Kill(); } catch { } }
                        return Failure(cancellationRequested ? "cancelled" : "timeout");
                    }
                    p.WaitForExit(); // Drain asynchronous stdout before inspecting the final result.
                    lock (resultGate)
                    {
                        if (badOutput || result == null || p.ExitCode != 0) return Failure("client_error");
                        return result;
                    }
                }
            }
            catch { return Failure("client_error"); }
            finally { lock (processGate) activeProcess = null; }
        }

        private static ConnectionReply Failure(string code)
        {
            return new ConnectionReply { @event = "result", code = code, ok = false, error = code == "cancelled" ? "操作已取消。" : (code == "timeout" ? "连接超时，请重试。" : "连接失败，请重试。") };
        }

        internal static string EnsureScript(string dataDir)
        {
            string target = Path.Combine(dataDir, "accounts.js");
            byte[] bytes;
            using (Stream source = Assembly.GetExecutingAssembly().GetManifestResourceStream("accounts.js"))
            {
                if (source == null) throw new InvalidOperationException("Missing account connector resource.");
                using (var memory = new MemoryStream()) { source.CopyTo(memory); bytes = memory.ToArray(); }
            }
            bool same = false;
            if (File.Exists(target))
            {
                byte[] current = File.ReadAllBytes(target);
                same = current.Length == bytes.Length;
                for (int i = 0; same && i < bytes.Length; i++) same = current[i] == bytes[i];
            }
            if (!same)
            {
                string temp = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
                File.WriteAllBytes(temp, bytes);
                if (File.Exists(target)) File.Replace(temp, target, null); else File.Move(temp, target);
            }
            return target;
        }
    }
}
