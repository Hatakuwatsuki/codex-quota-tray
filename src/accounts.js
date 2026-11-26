'use strict';
// Independent Codex homes; credentials are managed exclusively by Codex/keyring.
const fs = require('node:fs');
const path = require('node:path');
const crypto = require('node:crypto');
const childProcess = require('node:child_process');
const SLOTS = ['a','b','c','d','e'];

const ERRORS = {
  invalid_argument:'参数无效', missing_environment:'缺少运行环境', unsafe_path:'账号目录不安全',
  busy:'账号操作正在进行或上次操作未正常结束', not_connected:'账号尚未连接',
  already_connected:'账号已连接', no_candidate:'没有待确认账号', duplicate_account:'该账号已连接到另一个位置',
  identity_mismatch:'账号身份已改变', identity_unavailable:'无法确认账号身份',
  auth_failed:'账号授权失败', invalid_url:'授权地址不安全', timeout:'操作超时',
  cancelled:'操作已取消', server_error:'账号服务暂不可用', invalid_response:'账号服务返回异常',
  storage_error:'账号资料无法读写', codex_launch_failed:'Codex 查询程序无法启动'
};
class SafeError extends Error { constructor(code) { super(code); this.code = code; } }
function fail(code) { throw new SafeError(code); }
function safeId(s) { return typeof s === 'string' && /^[a-zA-Z0-9_-]{1,200}$/.test(s); }
function emailMask(email) {
  if (typeof email !== 'string') return '';
  const match = /^([a-zA-Z0-9][a-zA-Z0-9.!#$%&'*+\/=\?^_`{|}~-]*)@([a-zA-Z0-9](?:[a-zA-Z0-9.-]{0,251}[a-zA-Z0-9])?)$/.exec(email);
  if (!match) return '';
  const local = match[1];
  const prefix = local.length > 2 && /^[a-zA-Z0-9]{2}/.test(local) ? local.slice(0,2) : local[0];
  return prefix + '***@' + match[2].toLowerCase();
}
function plan(s) { return typeof s === 'string' && /^[a-z0-9_]{1,64}$/.test(s) ? s : ''; }
function validIdentity(x) {
  return x && safeId(x.userId) && safeId(x.accountId) && typeof x.emailMask === 'string' &&
    (x.emailMask === '' || /^[a-zA-Z0-9]{1,2}\*\*\*@[a-zA-Z0-9.-]{1,253}$/.test(x.emailMask)) &&
    typeof x.plan === 'string' && plan(x.plan) === x.plan;
}
function same(a,b) { return a.userId === b.userId && a.accountId === b.accountId; }
function publicIdentity(x) { return {userId:x.userId,accountId:x.accountId,emailMask:x.emailMask,plan:x.plan}; }
function assertPath(p) {
  const absolute = path.resolve(p);
  let current = path.parse(absolute).root;
  for (const segment of absolute.slice(current.length).split(path.sep).filter(Boolean)) {
    current = path.join(current, segment);
    try { const st = fs.lstatSync(current); if (st.isSymbolicLink()) fail('unsafe_path'); }
    catch (e) { if (e instanceof SafeError) throw e; if (e.code !== 'ENOENT') fail('unsafe_path'); }
  }
  return absolute;
}
// Node's lstat identifies symlinks/junctions, but not every Windows reparse tag.
// Ask Windows for attributes as well; an unavailable check fails closed.
function rejectWindowsReparse(paths) {
  if (process.platform !== 'win32') return;
  const all = new Set();
  for (const p of paths) {
    let current = path.resolve(p);
    for (;;) { all.add(current); const parent = path.dirname(current); if (parent === current) break; current = parent; }
  }
  const input = JSON.stringify(Array.from(all));
  const helper = process.env.QUOTA_PATH_CHECKER;
  if (helper) {
    const result = childProcess.spawnSync(helper, ['--check-account-paths'],
      {input, encoding:'utf8', windowsHide:true, timeout:10000, maxBuffer:4096});
    if (result.error || result.status !== 0) fail('unsafe_path');
    return;
  }
  const systemRoot = process.env.SystemRoot || process.env.SYSTEMROOT;
  if (!systemRoot) fail('unsafe_path');
  const powershell = path.join(systemRoot,'System32','WindowsPowerShell','v1.0','powershell.exe');
  const script = '$ErrorActionPreference="Stop"; [Console]::InputEncoding=[Text.Encoding]::UTF8; try { $paths=ConvertFrom-Json ([Console]::In.ReadToEnd()); foreach($p in $paths) { if(Test-Path -LiteralPath $p) { $a=(Get-Item -Force -LiteralPath $p).Attributes; if(($a -band [IO.FileAttributes]::ReparsePoint) -ne 0) {exit 3} } }; exit 0 } catch { exit 4 }';
  const result = childProcess.spawnSync(powershell,['-NoLogo','-NoProfile','-NonInteractive','-Command',script],{input,encoding:'utf8',windowsHide:true,timeout:10000,maxBuffer:4096});
  if (result.error || result.status !== 0) fail('unsafe_path');
}
function ensureDirectory(p) { assertPath(p); fs.mkdirSync(p, {recursive:true}); assertPath(p); if (!fs.statSync(p).isDirectory()) fail('unsafe_path'); }
function ensureConfig(home) {
  const file = path.join(home,'config.toml');
  const expected = 'cli_auth_credentials_store = "keyring"\nmodel_provider = "openai"\n';
  assertPath(file);
  try { fs.writeFileSync(file,expected,{flag:'wx',mode:0o600}); }
  catch(e) {
    if (e.code !== 'EEXIST') throw e;
    assertPath(file);
    if (!fs.lstatSync(file).isFile() || fs.statSync(file).size > 1024 || fs.readFileSync(file,'utf8') !== expected) fail('unsafe_path');
  }
}
function assertNoPlaintext(home) {
  const file = path.join(home,'auth.json');
  try { fs.lstatSync(file); fail('auth_failed'); }
  catch(e) { if (e instanceof SafeError) throw e; if (e.code !== 'ENOENT') fail('unsafe_path'); }
}
function readState(root) {
  const file = path.join(root,'accounts.json'); assertPath(file);
  let raw;
  try { raw = fs.readFileSync(file,'utf8'); } catch(e) { if (e.code === 'ENOENT') return {version:1,accounts:{}}; fail('storage_error'); }
  if (Buffer.byteLength(raw) > 32768) fail('storage_error');
  let state; try { state = JSON.parse(raw); } catch (_) { fail('storage_error'); }
  if (!state || state.version !== 1 || !state.accounts || typeof state.accounts !== 'object') fail('storage_error');
  const out = {version:1,accounts:{}};
  for (const slot of SLOTS) {
    const x = state.accounts[slot]; if (!x) continue;
    if (typeof x.connected !== 'boolean' || !validIdentity(x.identity)) fail('storage_error');
    out.accounts[slot] = {connected:x.connected,identity:publicIdentity(x.identity)};
    readSource(x,out.accounts[slot],slot);
    if (x.candidate) {
      if (!validIdentity(x.candidate.identity)) fail('storage_error');
      out.accounts[slot].candidate = {identity:publicIdentity(x.candidate.identity)};
      readSource(x.candidate,out.accounts[slot].candidate,slot);
    }
    if (x.connected && typeof x.confirmedAt === 'number' && Number.isSafeInteger(x.confirmedAt)) out.accounts[slot].confirmedAt = x.confirmedAt;
  }
  return out;
}
// Persist only source location and masked identity; never read or copy credential files.
function readSource(x,out,slot) {
  if (x.source === 'current') {
    if (typeof x.currentHome !== 'string' || !path.isAbsolute(x.currentHome) || x.currentHome.length > 4096) fail('storage_error');
    out.source = 'current'; out.currentHome = x.currentHome;
  } else {
    if (x.source !== undefined && x.source !== 'independent') fail('storage_error');
    if (x.home !== undefined) {
      if (typeof x.home !== 'string' || !new RegExp('^home-' + slot + '-[a-f0-9]{32}$').test(x.home)) fail('storage_error');
      out.home = x.home;
    }
  }
}
function sourceOf(x) { return x && x.source === 'current' ? 'current' : 'independent'; }
function candidateOf(x) { return x && (x.candidate || (!x.connected ? x : null)); }
function duplicate(state,slot,found) {
  return SLOTS.some(s => s !== slot && state.accounts[s] &&
    [state.accounts[s],candidateOf(state.accounts[s])].some(x => x && x.identity.userId === found.userId));
}
function currentLocation(options) {
  const home = options.currentHome || process.env.CODEX_HOME || (process.env.USERPROFILE && path.join(process.env.USERPROFILE,'.codex'));
  if (!home || typeof home !== 'string' || !path.isAbsolute(home)) fail('missing_environment');
  const resolved = assertPath(home); rejectWindowsReparse([resolved]);
  if (!fs.existsSync(resolved) || !fs.statSync(resolved).isDirectory()) fail('auth_failed');
  return fs.realpathSync(resolved);
}
function saveState(root,state) {
  const file = path.join(root,'accounts.json'); assertPath(file);
  const tmp = path.join(root,'.accounts-' + crypto.randomBytes(16).toString('hex') + '.tmp');
  fs.writeFileSync(tmp, JSON.stringify(state), {flag:'wx',mode:0o600});
  try { assertPath(file); fs.renameSync(tmp,file); } catch(e) { try { fs.unlinkSync(tmp); } catch (_) {} throw e; }
}
function deadProcess(pid) {
  if (!Number.isSafeInteger(pid) || pid <= 0 || pid > 2147483647) return false;
  try { process.kill(pid,0); return false; } catch(e) { return e.code === 'ESRCH'; }
}
function readLockOwner(dir) {
  const file = path.join(dir,'owner.json'); assertPath(dir); assertPath(file);
  try {
    if (!fs.lstatSync(file).isFile() || fs.statSync(file).size > 4096) return null;
    const owner = JSON.parse(fs.readFileSync(file,'utf8'));
    if (!owner || !['list','login','query','confirm','cancel','discover','bind-current'].includes(owner.command) ||
        (['list','discover'].includes(owner.command) ? owner.slot !== undefined : !SLOTS.includes(owner.slot)) ||
        typeof owner.nonce !== 'string' || !/^[a-f0-9]{48}$/.test(owner.nonce)) return null;
    return owner;
  } catch (_) { return null; }
}
function recoverLock(root,dir) {
  const guard = path.join(root,'.lock-recovery'); assertPath(guard);
  try { fs.mkdirSync(guard); } catch (_) { fail('busy'); }
  try {
    const first = readLockOwner(dir);
    if (!first || !deadProcess(first.pid) || !deadProcess(first.childPid)) fail('busy');
    const second = readLockOwner(dir);
    if (!second || first.nonce !== second.nonce || first.command !== second.command || first.slot !== second.slot || first.pid !== second.pid || first.childPid !== second.childPid ||
        !deadProcess(second.pid) || !deadProcess(second.childPid)) fail('busy');
    const isolated = path.join(root,'.lock-stale-' + crypto.randomBytes(24).toString('hex'));
    assertPath(isolated);
    try { fs.renameSync(dir,isolated); } catch (_) { fail('busy'); }
    // Leave the isolated lock markers intact; never remove account data.
  } finally { try { fs.rmdirSync(guard); } catch (_) {} }
}
function acquire(root,command,slot) {
  const dir = path.join(root,'.lock'); assertPath(dir);
  try { fs.mkdirSync(dir); } catch(e) {
    if (e.code !== 'EEXIST') throw e;
    recoverLock(root,dir);
    try { fs.mkdirSync(dir); } catch (_) { fail('busy'); }
  }
  const nonce = crypto.randomBytes(24).toString('hex');
  const owner = {command,slot,nonce,pid:process.pid,childPid:null};
  try { fs.writeFileSync(path.join(dir,'owner.json'),JSON.stringify(owner),{flag:'wx'}); }
  catch(e) { try { fs.rmdirSync(dir); } catch(_) {} throw e; }
  return {dir,nonce,owner};
}
function recordChild(lock,pid) {
  if (!Number.isSafeInteger(pid) || pid <= 0 || pid > 2147483647) return;
  const file = path.join(lock.dir,'owner.json'), temp = path.join(lock.dir,'owner-' + lock.nonce + '.tmp');
  assertPath(file); assertPath(temp);
  const current = readLockOwner(lock.dir);
  if (!current || current.nonce !== lock.nonce) fail('busy');
  lock.owner.childPid = pid;
  fs.writeFileSync(temp,JSON.stringify(lock.owner),{flag:'wx'});
  try { fs.renameSync(temp,file); } catch(e) { try { fs.unlinkSync(temp); } catch (_) {} throw e; }
}
function release(lock) {
  const owner = path.join(lock.dir,'owner.json'), cancel = path.join(lock.dir,'cancel');
  assertPath(lock.dir); assertPath(owner); assertPath(cancel);
  let value; try { value = JSON.parse(fs.readFileSync(owner,'utf8')); } catch (_) { return; }
  if (value.nonce !== lock.nonce) return;
  try { fs.unlinkSync(cancel); } catch(e) { if (e.code !== 'ENOENT') return; }
  try { fs.unlinkSync(owner); fs.rmdirSync(lock.dir); } catch (_) {}
}
function cancelled(lock) {
  const file = path.join(lock.dir,'cancel'); assertPath(file);
  try { return fs.readFileSync(file,'utf8') === lock.nonce; } catch(e) { if (e.code === 'ENOENT') return false; fail('unsafe_path'); }
}
function isolatedEnv(home) {
  const env = {};
  const allowed = new Set(['SYSTEMROOT','WINDIR','PATH','PATHEXT','USERPROFILE','HOMEDRIVE','HOMEPATH','LOCALAPPDATA','APPDATA','TEMP','TMP','COMSPEC','NUMBER_OF_PROCESSORS','PROCESSOR_ARCHITECTURE','HTTP_PROXY','HTTPS_PROXY','ALL_PROXY','NO_PROXY','http_proxy','https_proxy','all_proxy','no_proxy']);
  for (const key of Object.keys(process.env)) if (allowed.has(key.toUpperCase())) env[key] = process.env[key];
  env.CODEX_HOME = home;
  return env;
}
class Rpc {
  constructor(exe,home,spawn,abort,currentSource) {
    this.pending = new Map(); this.notices = []; this.next = 1; this.buffer = ''; this.outputBytes = 0; this.closed = false;
    const args = currentSource ? ['-c','model_provider="openai"','app-server','--stdio'] : ['-c','cli_auth_credentials_store="keyring"','-c','model_provider="openai"','app-server','--stdio'];
    this.proc = spawn(exe,args,{cwd:home,env:isolatedEnv(home),stdio:['pipe','pipe','pipe'],windowsHide:true});
    this.exit = new Promise(resolve => { this.proc.once('exit',() => { this.closed = true; this.stop('server_error'); resolve(); }); this.proc.once('error',() => { this.closed = true; this.stop('codex_launch_failed'); resolve(); }); });
    this.proc.stdout.on('data',d => this.data(d));
    this.proc.stderr.on('data',() => {});
    this.proc.stdin.on('error',() => this.stop('server_error'));
    this.onAbort = () => this.stop(abort.reason instanceof SafeError ? abort.reason.code : 'cancelled');
    abort.addEventListener('abort',this.onAbort,{once:true});
    if (abort.aborted) this.onAbort();
  }
  stop(code) { this.failure = this.failure || code; for (const p of this.pending.values()) p.reject(new SafeError(this.failure)); this.pending.clear(); if (this.noticeWait) { const n = this.noticeWait; this.noticeWait = null; n.reject(new SafeError(this.failure)); } }
  data(chunk) {
    if (this.failure) return;
    this.outputBytes += chunk.length;
    if (this.outputBytes > 4194304) return this.stop('invalid_response');
    this.buffer += chunk.toString('utf8');
    if (this.buffer.length > 1048576) return this.stop('invalid_response');
    let i; while ((i = this.buffer.indexOf('\n')) >= 0) {
      const line = this.buffer.slice(0,i); this.buffer = this.buffer.slice(i+1);
      if (!line.trim()) continue;
      let m; try { m = JSON.parse(line); } catch (_) { return this.stop('invalid_response'); }
      if (m && this.pending.has(m.id)) { const p = this.pending.get(m.id); this.pending.delete(m.id); if (m.error || !m.result || typeof m.result !== 'object') p.reject(new SafeError('server_error')); else p.resolve(m.result); }
      else if (m && m.method === 'account/login/completed') { if (this.noticeWait) { const n = this.noticeWait; this.noticeWait = null; n.resolve(m.params); } else if (this.notices.length < 4) this.notices.push(m.params); else return this.stop('invalid_response'); }
    }
  }
  call(method,params) {
    if (this.failure) return Promise.reject(new SafeError(this.failure));
    const id = this.next++;
    return new Promise((resolve,reject) => { this.pending.set(id,{resolve,reject}); try { this.proc.stdin.write(JSON.stringify({jsonrpc:'2.0',id,method,params})+'\n'); } catch (_) { this.pending.delete(id); reject(new SafeError('server_error')); } });
  }
  async init() { await this.call('initialize',{clientInfo:{name:'quota-tray',version:'1.0.0'},capabilities:{experimentalApi:true}}); this.proc.stdin.write(JSON.stringify({jsonrpc:'2.0',method:'initialized',params:{}})+'\n'); }
  notification() { if (this.failure) return Promise.reject(new SafeError(this.failure)); if (this.notices.length) return Promise.resolve(this.notices.shift()); return new Promise((resolve,reject) => { this.noticeWait = {resolve,reject}; }); }
  async close() {
    if (!this.closed) { try { this.proc.kill(); } catch (_) {} }
    if (this.closed) return true;
    let timer;
    const result = await Promise.race([this.exit.then(() => true),new Promise(resolve => { timer = setTimeout(() => resolve(false),3000); })]);
    clearTimeout(timer);
    return result;
  }
}
async function identity(rpc) {
  const account = await rpc.call('account/read',{refreshToken:false});
  if (!account.account || account.account.type !== 'chatgpt') fail('auth_failed');
  const sessions = await rpc.call('accountSession/list',{refreshWorkspaceMetadata:false});
  if (!Array.isArray(sessions.sessions) || !safeId(sessions.activeSessionId)) fail('identity_unavailable');
  const active = sessions.sessions.filter(s => s && s.isActive === true);
  if (active.length !== 1 || active[0].sessionId !== sessions.activeSessionId || !safeId(active[0].userId)) fail('identity_unavailable');
  const selected = active[0].selectedWorkspaceAccountId;
  const routing = account.workspaceRouting && account.workspaceRouting.chatgptAccountId;
  if (selected && routing && selected !== routing) fail('identity_mismatch');
  const accountId = selected || routing;
  if (!safeId(accountId)) fail('identity_unavailable');
  return {userId:active[0].userId,accountId,emailMask:emailMask(account.account.email),plan:plan(account.account.planType)};
}
function win(w) {
  if (w == null) return null;
  if (!w || !Number.isFinite(w.usedPercent) || w.usedPercent < 0 || w.usedPercent > 100 ||
      (!Number.isSafeInteger(w.windowDurationMins) || w.windowDurationMins <= 0) ||
      (w.resetsAt != null && !Number.isSafeInteger(w.resetsAt))) fail('invalid_response');
  return {used:w.usedPercent,windowMins:w.windowDurationMins == null ? null : w.windowDurationMins,resetsAt:w.resetsAt == null ? null : w.resetsAt};
}
function safeUnix(v) {
  return Number.isSafeInteger(v) && v >= 0 && v <= 253402300799 ? v : 0;
}
function parseResetCredits(raw) {
  if (!raw || typeof raw !== 'object') return null;
  const availableCount = Number.isSafeInteger(raw.availableCount) && raw.availableCount >= 0 ? raw.availableCount : 0;
  const hasDetails = Array.isArray(raw.credits);
  const credits = [];
  if (hasDetails) {
    for (const c of raw.credits) {
      if (!c || typeof c !== 'object' || typeof c.id !== 'string' || !c.id) continue;
      credits.push({
        id: String(c.id).slice(0, 128),
        grantedAt: safeUnix(c.grantedAt),
        expiresAt: safeUnix(c.expiresAt),
        status: typeof c.status === 'string' ? c.status : 'unknown'
      });
      if (credits.length >= 50) break;
    }
  }
  return { availableCount, hasDetails, credits };
}
function validateUrl(s) {
  if (typeof s !== 'string' || s.length > 8192) fail('invalid_url');
  let u; try { u = new URL(s); } catch (_) { fail('invalid_url'); }
  const authority = /^https:\/\/([^/?#]*)/i.exec(s);
  if (!authority || authority[1].includes('@') || u.protocol !== 'https:' || !['auth.openai.com','chatgpt.com'].includes(u.hostname) || u.username || u.password || (u.port && u.port !== '443')) fail('invalid_url');
  return s;
}
async function requestCancel(root,slot) {
  const dir = path.join(root,'.lock'), ownerFile = path.join(dir,'owner.json'); assertPath(dir); assertPath(ownerFile);
  let owner; try { owner = JSON.parse(fs.readFileSync(ownerFile,'utf8')); } catch (_) { fail('busy'); }
  if (!owner || owner.command !== 'login' || owner.slot !== slot || !/^[a-f0-9]{48}$/.test(owner.nonce || '')) fail('busy');
  const marker = path.join(dir,'cancel'); assertPath(marker);
  try { fs.writeFileSync(marker,owner.nonce,{flag:'wx'}); } catch(e) { if (e.code !== 'EEXIST') fail('busy'); }
  const end = Date.now() + 5000;
  while (Date.now() < end) { await new Promise(r => setTimeout(r,50)); if (!fs.existsSync(dir)) return; }
  fail('busy');
}
async function run(command,slot,options = {}) {
  const emit = options.emit || (event => process.stdout.write(JSON.stringify(event)+'\n'));
  let lock, rpc, keepLock = false, timer, watch;
  const abort = new AbortController();
  const externalAbort = () => abort.abort(new SafeError('cancelled'));
  if (options.signal) { if (options.signal.aborted) externalAbort(); else options.signal.addEventListener('abort',externalAbort,{once:true}); }
  try {
    if (!['list','login','query','confirm','cancel','discover','bind-current','unbind'].includes(command) || (['list','discover'].includes(command) ? slot !== undefined : !SLOTS.includes(slot))) fail('invalid_argument');
    const base = options.root || (process.env.LOCALAPPDATA && path.join(process.env.LOCALAPPDATA,'CodexQuotaTray','accounts'));
    if (!base) fail('missing_environment');
    const root = path.resolve(base);
    rejectWindowsReparse([root,...SLOTS.map(s => path.join(root,'home-'+s)),path.join(root,'accounts.json'),path.join(root,'.lock')]);
    ensureDirectory(root);
    // For safety all state and all credentials operations are globally serialized.
    try { lock = acquire(root,command,slot); }
    catch(e) { if (!(e instanceof SafeError) || e.code !== 'busy' || command !== 'cancel') throw e; await requestCancel(root,slot); lock = acquire(root,command,slot); }
    const state = readState(root), entry = state.accounts[slot];
    if (command === 'list') {
      emit({event:'result',ok:true,accounts:SLOTS.map(s => {
        const x = state.accounts[s], candidate = candidateOf(x), item = {slot:s,connected:!!(x && x.connected),pending:!!candidate};
        if (x && x.connected) { item.emailMask = x.identity.emailMask; item.plan = x.identity.plan; }
        if (x) {
          item.identity = publicIdentity(x.identity);
          item.source = sourceOf(x);
          if (candidate && x.connected) {
            item.candidateIdentity = publicIdentity(candidate.identity);
            item.candidateSource = sourceOf(candidate);
          }
        }
        return item;
      })}); return;
    }
    if (command === 'cancel') {
      if (entry) {
        if (entry.connected) delete entry.candidate; else delete state.accounts[slot];
        saveState(root,state);
      }
      emit({event:'result',ok:true,slot}); return;
    }
    if (command === 'unbind') {
      // Removes the binding record only; credential files stay untouched so an
      // accidental unbind never destroys another account's saved sign-in.
      if (!entry || !entry.connected) fail('not_connected');
      delete state.accounts[slot];
      saveState(root,state);
      emit({event:'result',ok:true,slot}); return;
    }
    if (command === 'query' && (!entry || !entry.connected)) fail('not_connected');
    const candidate = candidateOf(entry);
    if (command === 'confirm' && !candidate) fail('no_candidate');
    const selected = command === 'confirm' ? candidate : entry;
    const useCurrent = ['discover','bind-current'].includes(command) || (command !== 'login' && sourceOf(selected) === 'current');
    const newHome = command === 'login' && entry && entry.connected ? 'home-' + slot + '-' + crypto.randomBytes(16).toString('hex') : null;
    const home = useCurrent ? (['discover','bind-current'].includes(command) ? currentLocation(options) : assertPath(selected.currentHome)) : path.join(root,newHome || (selected && selected.home) || 'home-'+slot);
    if (useCurrent) {
      rejectWindowsReparse([home]);
      if (!fs.existsSync(home) || !fs.statSync(home).isDirectory()) fail('auth_failed');
    } else {
      const others = SLOTS.filter(s => s !== slot).map(s => path.join(root,'home-'+s));
      ensureDirectory(home);
      for (const other of others) assertPath(other);
      rejectWindowsReparse([home,...others,path.join(home,'config.toml'),path.join(home,'auth.json'),path.join(root,'accounts.json'),lock.dir]);
      const realHome = fs.realpathSync(home).toLowerCase();
      for (const other of others) if (fs.existsSync(other) && realHome === fs.realpathSync(other).toLowerCase()) fail('unsafe_path');
      ensureConfig(home);
      assertNoPlaintext(home);
    }
    const checkPlaintext = () => { if (!useCurrent) assertNoPlaintext(home); };
    const saveCandidate = found => {
      if (duplicate(state,slot,found)) fail('duplicate_account');
      if (abort.signal.aborted || cancelled(lock)) fail('cancelled');
      const next = {connected:false,identity:found};
      if (useCurrent) { next.source = 'current'; next.currentHome = home; }
      else if (newHome || (selected && selected.home)) next.home = newHome || selected.home;
      if (entry && entry.connected) { delete next.connected; entry.candidate = next; }
      else state.accounts[slot] = next;
      saveState(root,state);
      emit({event:'candidate',ok:true,slot,source:sourceOf(next),identity:publicIdentity(found)});
    };
    const exe = options.exe || process.env.CODEX_CLI_PATH;
    if (!exe || typeof exe !== 'string') fail('missing_environment');
    if (abort.signal.aborted) fail('cancelled');
    timer = setTimeout(() => abort.abort(new SafeError('timeout')), options.timeoutMs || (command === 'login' ? 180000 : 25000));
    watch = setInterval(() => { try { if (cancelled(lock)) abort.abort(new SafeError('cancelled')); } catch (_) { abort.abort(new SafeError('unsafe_path')); } },100);
    rpc = new Rpc(exe,home,options.spawn || childProcess.spawn,abort.signal,useCurrent);
    recordChild(lock,rpc.proc.pid);
    await rpc.init();
    if (command === 'discover' || command === 'bind-current') {
      const found = await identity(rpc);
      if (abort.signal.aborted || cancelled(lock)) fail('cancelled');
      if (command === 'discover') emit({event:'result',ok:true,source:'current',identity:publicIdentity(found)});
      else saveCandidate(found);
      return;
    }
    if (command === 'login') {
      // Existing connected credentials remain untouched in their original home.
      const started = await rpc.call('account/login/start',{type:'chatgpt'});
      if (started.type !== 'chatgpt' || !safeId(started.loginId)) fail('invalid_response');
      emit({event:'login',slot,authUrl:validateUrl(started.authUrl)});
      const done = await rpc.notification();
      if (!done || done.loginId !== started.loginId || done.success !== true) fail('auth_failed');
      const found = await identity(rpc);
      checkPlaintext();
      saveCandidate(found); return;
    }
    const before = await identity(rpc);
    checkPlaintext();
    if (!same(before,selected.identity)) fail('identity_mismatch');
    if (command === 'confirm') {
      if (duplicate(state,slot,before)) fail('duplicate_account');
      if (abort.signal.aborted || cancelled(lock)) fail('cancelled');
      const confirmed = {connected:true,identity:before,confirmedAt:Date.now()};
      readSource(selected,confirmed,slot);
      state.accounts[slot] = confirmed; saveState(root,state);
      emit({event:'result',ok:true,slot,identity:publicIdentity(before)}); return;
    }
    const skipDetails = process.env.CODEX_QUOTA_CREDIT_DETAILS === '0';
    let limits;
    try {
      limits = await rpc.call('account/rateLimits/read', skipDetails ? { excludeResetCreditDetails: true } : {});
    } catch (err) {
      if (skipDetails && err instanceof SafeError && err.code === 'server_error') {
        limits = await rpc.call('account/rateLimits/read', {});
      } else {
        throw err;
      }
    }
    if (limits.accountId != null && limits.accountId !== before.accountId) fail('identity_mismatch');
    const primary = win(limits.rateLimits && limits.rateLimits.primary), secondary = win(limits.rateLimits && limits.rateLimits.secondary);
    if (!limits.rateLimits || typeof limits.rateLimits !== 'object' || (!primary && !secondary)) fail('invalid_response');
    const resetCredits = parseResetCredits(limits.rateLimitResetCredits);
    const after = await identity(rpc);
    checkPlaintext();
    if (!same(before,after) || !same(after,entry.identity)) fail('identity_mismatch');
    const out = {event:'result',ok:true,slot,identity:publicIdentity(after),primary,secondary,at:Date.now()};
    if (resetCredits) out.resetCredits = resetCredits;
    emit(out);
  } catch(e) {
    const code = e instanceof SafeError && ERRORS[e.code] ? e.code : 'storage_error';
    const out = {event:'result',ok:false}; if (SLOTS.includes(slot)) out.slot = slot;
    out.code = code; out.error = ERRORS[code]; emit(out);
  } finally {
    clearTimeout(timer); clearInterval(watch);
    if (rpc && !await rpc.close()) keepLock = true;
    if (lock && !keepLock) release(lock);
    if (options.signal) options.signal.removeEventListener('abort',externalAbort);
  }
}
module.exports = {run};
if (require.main === module) {
  const args = process.argv.slice(2), controller = new AbortController();
  if (args.length > 2) { process.stdout.write(JSON.stringify({event:'result',ok:false,code:'invalid_argument',error:ERRORS.invalid_argument})+'\n'); }
  else {
    let input = ''; process.stdin.setEncoding('utf8');
    process.stdin.on('data',chunk => { input += chunk; if (input.length > 4096) { controller.abort(); return; } let i; while ((i = input.indexOf('\n')) >= 0) { const line = input.slice(0,i); input = input.slice(i+1); try { const m = JSON.parse(line); if (m && m.action === 'cancel') controller.abort(); } catch (_) {} } });
    process.stdin.on('end',() => { if (args[0] === 'login') controller.abort(); });
    process.stdin.on('error',() => controller.abort());
    run(args[0],args[1],{signal:controller.signal}).then(() => { process.stdin.pause(); }).catch(() => { process.stdout.write(JSON.stringify({event:'result',ok:false,code:'storage_error',error:ERRORS.storage_error})+'\n'); process.stdin.pause(); });
  }
}
