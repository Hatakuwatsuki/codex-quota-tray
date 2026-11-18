# CodexQuotaTray · GPT 额度显示器

Windows 托盘工具，可查看已绑定 Codex 账号的 5 小时和 7 天额度，支持多账号、分档提醒与中英日界面。

## 下载与升级

从 [Releases](https://github.com/Hatakuwatsuki/codex-quota-tray/releases) 下载版本号对应的 EXE。三种提示音已内嵌。
旧版运行时，将新版放到旧程序所在文件夹，保留下载文件名并双击。程序会退出旧版、替换原 EXE 并以原文件名重启。旧版已退出时，可以改名直接覆盖。
右键托盘可刷新、打开设置或退出。配置 Codex 和账号后即可查询额度。

## 编译

在仓库根目录运行 `powershell -File src/rebuild.ps1`，生成 `build/CodexQuotaTray.exe`。仓库里的 WAV 用作编译资源。
更多说明见 [使用说明](使用说明.txt)。

非官方工具，与 OpenAI 无隶属关系。MIT 许可。