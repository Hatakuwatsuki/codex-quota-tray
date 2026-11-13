# Codex Quota Tray 额度显示器

A tiny Windows notification-area app that shows how much Codex / ChatGPT quota you have left — and warns you before it runs out.

The problem it solves: quota runs out mid-conversation and your session gets cut off. This tool sits in the tray, shows the remaining 5-hour and 7-day allowance at a glance, and pops a notification at the points you choose.

> Unofficial tool. Not affiliated with or endorsed by OpenAI.

---

## What it does

- **Tray gauge** — two arcs (5-hour and 7-day quota) or a single ring on plans that only have a weekly allowance. Colour follows what's left: green → amber → coral.
- **Hover for numbers** — remaining percentage and time until reset, in one line per allowance.
- **Alerts** — a self-drawn toast card slides in from the right with your own sound, at the thresholds you pick. Four levels: Off / Low / Medium / High.
- **Used-up alerts** — a separate card when an allowance hits zero.
- **Details window** — the numbers plus reset times, following the system light/dark theme.
- **Three languages** — 中文 / 日本語 / English, switched from the menu, follows the Windows display language by default.
- **Portable** — a single exe, no installer, no service, no admin rights. All data (logs, cache, settings) lives next to the exe.

## Requirements

- Windows 10 or 11
- [Codex desktop app](https://developers.openai.com/codex/) installed and **signed in with a ChatGPT account** (Plus / Pro)
- .NET Framework 4.0 or later (built into Windows 10/11)

That's it. The app reads quota from the local Codex app-server using your existing sign-in — it never asks for credentials, never touches your account, and works offline from your side apart from the quota query itself.

> Accounts signed in with a bare API key instead of a ChatGPT account don't expose rate limits. In that case the tray will show an error instead of numbers; that's a limit of the Codex API, not a bug.

## Install

1. Download `CodexQuotaTray-*.exe` from [Releases](../../releases).
2. Put it in a folder you can write to (Documents, Desktop, a portable drive — anywhere except `Program Files`).
3. Run it. A ring appears in the notification area.

On first run it creates a desktop shortcut. Windows SmartScreen may warn you because the exe isn't code-signed — choose *More info → Run anyway*.

Keep the `.wav` files in the same folder as the exe if you want the custom alert sounds.

## Configuration

Right-click the tray icon:

```
Refresh / Quota details
──────
Quota alerts ▸ Off · Low · Medium · High
Start with Windows
Language ▸ Auto · 中文 · 日本語 · English
──────
Create desktop shortcut
──────
Exit
```

Alert thresholds are counted on **used** percentage and phrased on **remaining**:

| Level | Fires when remaining hits |
|---|---|
| Low (default) | 25%, 10% |
| Medium | 75%, 25%, 10% |
| High | 75%, 50%, 25%, 10% |

On Plus plans the 7-day allowance always uses its own fixed pair (25% and 10%), regardless of the level. On Pro plans — where the weekly allowance is the only one — the level applies to it.

Settings are stored in `settings.txt` next to the exe:

```ini
notifyLevel=low      # off / low / medium / high
language=auto        # auto / zh / ja / en
```

Alert sounds are plain WAV files next to the exe:

| File | Used for |
|---|---|
| `notify.wav` | threshold alerts |
| `notify-5h-empty.wav` | 5-hour allowance used up |
| `notify-7d-empty.wav` | 7-day allowance used up |

WAV only, 16-bit PCM. Drop in your own and restart.

## Build

No SDK, no NuGet. `csc.exe` from the .NET Framework 4 toolchain is enough:

```powershell
$fw = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'

& "$fw" /nologo /target:winexe `
  /out:CodexQuotaTray.exe `
  /win32icon:src\icon.ico `
  /resource:src\quota.js,quota.js `
  /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll `
  src\Program.cs
```

`src\rebuild.ps1` does the same and additionally regenerates the icon set.

Debug switches (each exits without starting the tray):

| Switch | Purpose |
|---|---|
| `--preview-detail` | show the details window with fake data |
| `--preview-menu <png>` | render the right-click menu to an image |
| `--preview-pro <dir>` | render the single-ring variant |
| `--test-notify <file>` | run the alert logic over fake sequences, write a report |
| `--test-toast <dir> <wav>` | render the toast card to an image |
| `--test-toast-live <wav>` | show a real toast with animation |
| `--demo-toast <kind>` | 5h-empty / 7d-empty / 25 / 10 |

## How it works

```
tray app  →  node quota.js  →  codex app-server --stdio  →  account/rateLimits/read
```

`quota.js` locates `codex.exe` under `%LOCALAPPDATA%\OpenAI\Codex\bin`, starts the local app-server, and asks for rate limits. Node is taken from the exe folder, the system, or the runtime bundled with Codex — no separate Node install required.

The exe embeds `quota.js` as a resource and unpacks it next to itself on first run, which is why a single file is enough.

## License

MIT — see [LICENSE](LICENSE).

---

## 中文说明

Windows 托盘小工具：读取本机 Codex 桌面版的登录状态，显示 5 小时 / 7 天额度还剩多少、什么时候恢复，并在快用完时弹出提醒。

- 图标随额度变色，桌面快捷方式图标同步变化；悬停看具体数字和恢复时间
- 提醒分四档（关闭 / 低 / 中 / 高），默认低档：剩 25% 和剩 10% 各提醒一次
- 额度真的用光时再弹一次「已耗尽」
- 支持中文 / 日文 / 英文，右键菜单里切换
- 绿色便携，单个 exe，不需要安装、不需要管理员权限

使用前提：装了 Codex 桌面版，并且用 ChatGPT 账号登录过。纯 API Key 登录的账号读不到额度数据（接口本身不返回）。

完整说明见 [使用说明.txt](使用说明.txt)。
