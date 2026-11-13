#!/usr/bin/env node
/**
 * 额度查询（供托盘程序调用）
 *
 * 输出一行 JSON，方便 PowerShell 直接 ConvertFrom-Json：
 *   {"ok":true,"plan":"plus","primary":{...},"secondary":{...},"at":1723...}
 * 失败时：
 *   {"ok":false,"error":"..."}
 *
 * 数据来源：codex app-server 的 account/rateLimits/read
 * （桌面端设置里看不到额度的原因见 README，这个接口照样能拿到数据）
 */
const { spawn } = require("child_process");
const fs = require("fs");
const path = require("path");

const TIMEOUT_MS = 15000;

function fail(msg) {
  process.stdout.write(JSON.stringify({ ok: false, error: String(msg) }) + "\n");
  process.exit(0);
}

/** 找 codex.exe：优先 CODEX_CLI_PATH，其次扫 bin 下最新的那版 */
function findCodex() {
  if (process.env.CODEX_CLI_PATH && fs.existsSync(process.env.CODEX_CLI_PATH)) {
    return process.env.CODEX_CLI_PATH;
  }
  const binRoot = path.join(process.env.LOCALAPPDATA || "", "OpenAI", "Codex", "bin");
  if (!fs.existsSync(binRoot)) return null;
  const cands = [];
  for (const name of fs.readdirSync(binRoot)) {
    const exe = path.join(binRoot, name, "codex.exe");
    try {
      if (fs.existsSync(exe)) {
        cands.push({ exe, mtime: fs.statSync(exe).mtimeMs });
      }
    } catch {}
  }
  if (!cands.length) return null;
  cands.sort((a, b) => b.mtime - a.mtime);
  return cands[0].exe;
}

const exe = findCodex();
if (!exe) fail("找不到 codex.exe（Codex 桌面端没装？）");

const proc = spawn(exe, ["app-server", "--stdio"], { stdio: ["pipe", "pipe", "pipe"] });
let buf = "";
let done = false;

const timer = setTimeout(() => {
  if (done) return;
  done = true;
  try {
    proc.kill();
  } catch {}
  fail("查询超时（Codex 没响应或网络不通）");
}, TIMEOUT_MS);

function windowOf(w) {
  if (!w) return null;
  const pct = typeof w.usedPercent === "number" ? w.usedPercent : null;
  const mins = typeof w.windowDurationMins === "number" ? w.windowDurationMins : null;
  const resetsAt = typeof w.resetsAt === "number" ? w.resetsAt : null;
  return { used: pct, windowMins: mins, resetsAt };
}

proc.stdout.on("data", (d) => {
  buf += d.toString();
  let idx;
  while ((idx = buf.indexOf("\n")) >= 0) {
    const line = buf.slice(0, idx);
    buf = buf.slice(idx + 1);
    if (!line.trim()) continue;
    let msg;
    try {
      msg = JSON.parse(line);
    } catch {
      continue;
    }

    if (msg.id === 1) {
      proc.stdin.write(JSON.stringify({ jsonrpc: "2.0", method: "initialized", params: {} }) + "\n");
      proc.stdin.write(
        JSON.stringify({ jsonrpc: "2.0", id: 2, method: "account/rateLimits/read", params: {} }) + "\n",
      );
    }

    if (msg.id === 2 && !done) {
      done = true;
      clearTimeout(timer);
      const rl = msg.result && msg.result.rateLimits;
      try {
        proc.kill();
      } catch {}
      if (!rl) {
        fail("接口没返回额度数据：" + JSON.stringify(msg.error || msg.result || {}));
      }
      process.stdout.write(
        JSON.stringify({
          ok: true,
          plan: rl.planType || "",
          primary: windowOf(rl.primary),
          secondary: windowOf(rl.secondary),
          reached: rl.rateLimitReachedType || null,
          at: Date.now(),
        }) + "\n",
      );
      process.exit(0);
    }
  }
});

proc.stderr.on("data", () => {});
proc.on("error", (e) => {
  if (done) return;
  done = true;
  clearTimeout(timer);
  fail("启动 codex 失败：" + e.message);
});

proc.stdin.write(
  JSON.stringify({
    jsonrpc: "2.0",
    id: 1,
    method: "initialize",
    params: { clientInfo: { name: "quota-tray", version: "1.0.0" } },
  }) + "\n",
);
