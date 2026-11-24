using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace QuotaTrayApp
{
    internal enum UpdatePhase { Idle, Checking, Current, Available, Downloading, Installing, CheckFailed, InstallFailed }

    internal sealed class UpdateStatus
    {
        private const int CurrentNoticeMilliseconds = 15000;
        private readonly long createdAt = Stopwatch.GetTimestamp();
        internal readonly UpdatePhase Phase;
        internal readonly Version Version;
        internal readonly bool Manual;
        internal UpdateStatus(UpdatePhase phase, Version version = null, bool manual = false)
        { Phase = phase; Version = version; Manual = manual; }
        internal bool Busy { get { return Phase == UpdatePhase.Checking || Phase == UpdatePhase.Downloading || Phase == UpdatePhase.Installing; } }

        internal int CurrentNoticeRemaining(long now)
        {
            if (Phase != UpdatePhase.Current || !Manual) return 0;
            double elapsed = Math.Max(0, now - createdAt) * 1000.0 / Stopwatch.Frequency;
            return (int)Math.Max(0, Math.Ceiling(CurrentNoticeMilliseconds - elapsed));
        }
    }

    internal sealed class ReleaseAsset
    {
        internal Version Version;
        internal Uri Url;
        internal long Size;
        internal string Digest;
    }

    internal sealed class ReleaseUpdates
    {
        internal const string Repository = "Hatakuwatsuki/codex-quota-tray";
        internal const string HelperName = "QuotaDisplayUpdate.exe";
        internal const string UpdatedMarkerName = "release-updated.txt";
        internal static readonly TimeSpan AutomaticInterval = TimeSpan.FromHours(1);
        private const int MaxAssetSize = 32 * 1024 * 1024;
        private const int MaxMetadataSize = 256 * 1024;
        private readonly object gate = new object();
        private readonly string executable;
        private readonly string cache;
        private readonly string notice;
        private readonly Action<string> log;
        private readonly Version current;
        private UpdateStatus state = new UpdateStatus(UpdatePhase.Idle);
        private DateTime lastCheck;
        private ReleaseAsset asset;
        private Version lastNotified;
        private Version autoAttempted;
        private string stagedFile;
        private Version stagedVersion;
        private bool autoDownloading;
        private bool autoInstalled;

        internal ReleaseUpdates(string executable, string root, Action<string> log)
        {
            this.executable = executable;
            this.log = log;
            cache = Path.Combine(root, ".release-update");
            notice = Path.Combine(root, "release-notified.txt");
            current = typeof(ReleaseUpdates).Assembly.GetName().Version;
            try { if (File.Exists(notice)) Version.TryParse(File.ReadAllText(notice).Trim(), out lastNotified); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        internal UpdateStatus State { get { lock (gate) return state; } }

        internal static bool IsCheckDue(bool busy, bool manual, DateTime last, DateTime now)
        {
            return !busy && (manual || now - last >= AutomaticInterval);
        }

        internal void Check(bool manual)
        {
            lock (gate)
            {
                DateTime now = DateTime.UtcNow;
                if (!IsCheckDue(state.Busy, manual, lastCheck, now)) return;
                lastCheck = now;
                state = new UpdateStatus(UpdatePhase.Checking);
            }
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    ReleaseAsset available = CheckLatest(current);
                    lock (gate)
                    {
                        asset = available;
                        state = available == null ? new UpdateStatus(UpdatePhase.Current, null, manual)
                            : new UpdateStatus(UpdatePhase.Available, available.Version);
                    }
                }
                catch (Exception error)
                {
                    Report(error);
                    lock (gate)
                    {
                        state = asset == null ? new UpdateStatus(UpdatePhase.CheckFailed)
                            : new UpdateStatus(UpdatePhase.CheckFailed, asset.Version);
                    }
                }
            });
        }

        internal Version TakeNotification()
        {
            Version found;
            lock (gate)
            {
                if (state.Phase != UpdatePhase.Available || asset == null ||
                    (lastNotified != null && asset.Version <= lastNotified)) return null;
                lastNotified = found = asset.Version;
            }
            try { File.WriteAllText(notice, found.ToString()); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return found;
        }

        internal static Version ConsumeUpdatedVersion(string root)
        {
            if (String.IsNullOrEmpty(root)) return null;
            string mark = Path.Combine(root, UpdatedMarkerName);
            if (!File.Exists(mark)) return null;
            string raw = null;
            try
            {
                raw = File.ReadAllText(mark).Trim();
                File.Delete(mark);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            Version parsed;
            return !String.IsNullOrEmpty(raw) && Version.TryParse(raw, out parsed) ? parsed : null;
        }

        internal static bool ShouldStartAutoDownload(bool autoEnabled, UpdateStatus status, ReleaseAsset available,
            Version attempted, Version staged, bool downloading, bool installed)
        {
            return autoEnabled && !downloading && !installed && status != null && !status.Busy &&
                status.Phase == UpdatePhase.Available && available != null &&
                (staged == null || available.Version > staged) &&
                (attempted == null || available.Version > attempted);
        }

        internal static bool CanRunAutoInstall(bool autoEnabled, bool uiBusy, UpdateStatus status,
            Version currentVersion, Version staged, string file, bool downloading, bool installed)
        {
            return autoEnabled && !uiBusy && !downloading && !installed && status != null && !status.Busy &&
                staged != null && currentVersion != null && staged > currentVersion &&
                !String.IsNullOrEmpty(file) && File.Exists(file);
        }

        internal void StartAutoDownload(bool autoEnabled)
        {
            ReleaseAsset selected;
            lock (gate)
            {
                if (!ShouldStartAutoDownload(autoEnabled, state, asset, autoAttempted, stagedVersion, autoDownloading, autoInstalled))
                    return;
                selected = asset;
                autoAttempted = selected.Version;
                autoDownloading = true;
                state = new UpdateStatus(UpdatePhase.Downloading, selected.Version);
            }
            ThreadPool.QueueUserWorkItem(delegate
            {
                string file = Path.Combine(cache, "CodexQuotaTray-" + selected.Version + ".exe");
                try
                {
                    Directory.CreateDirectory(cache);
                    if ((File.GetAttributes(cache) & FileAttributes.ReparsePoint) != 0)
                        throw new IOException("The update cache is a redirected directory.");
                    DeleteDownload(file);
                    Download(selected, file);
                    lock (gate)
                    {
                        autoDownloading = false;
                        stagedFile = file;
                        stagedVersion = selected.Version;
                        state = new UpdateStatus(UpdatePhase.Available, selected.Version);
                    }
                }
                catch (Exception error)
                {
                    DeleteDownload(file);
                    Report(error);
                    lock (gate)
                    {
                        autoDownloading = false;
                        state = new UpdateStatus(UpdatePhase.InstallFailed, selected.Version);
                    }
                }
            });
        }

        internal bool TryAutoInstall(bool autoEnabled, bool uiBusy)
        {
            Version targetVersion;
            string file;
            lock (gate)
            {
                if (!CanRunAutoInstall(autoEnabled, uiBusy, state, current, stagedVersion, stagedFile, autoDownloading, autoInstalled))
                    return false;
                autoInstalled = true;
                targetVersion = stagedVersion;
                file = stagedFile;
                state = new UpdateStatus(UpdatePhase.Installing, targetVersion);
            }
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    Directory.CreateDirectory(cache);
                    if ((File.GetAttributes(cache) & FileAttributes.ReparsePoint) != 0)
                        throw new IOException("The update cache is a redirected directory.");
                    string helper = Path.Combine(cache, HelperName);
                    File.Copy(executable, helper, true);
                    string arguments = "--update-silent \"" + executable + "\" \"" + file + "\" \"" + targetVersion + "\"";
                    using (Process next = Process.Start(new ProcessStartInfo(helper, arguments)
                        { WorkingDirectory = cache, UseShellExecute = false }))
                    {
                        if (next == null) throw new IOException("Could not start the silent update.");
                        next.WaitForExit();
                        throw new IOException("The update closed before the running copy was replaced.");
                    }
                }
                catch (Exception error)
                {
                    DeleteDownload(file);
                    Report(error);
                    lock (gate)
                    {
                        autoInstalled = false;
                        stagedFile = null;
                        stagedVersion = null;
                        state = new UpdateStatus(UpdatePhase.InstallFailed, targetVersion);
                    }
                }
            });
            return true;
        }

        internal static void RunSilent(string helper, string target, string file, string versionText)
        {
            Version nextVersion;
            if (String.IsNullOrEmpty(helper) || String.IsNullOrEmpty(target) || String.IsNullOrEmpty(file) ||
                String.IsNullOrEmpty(versionText) || !Version.TryParse(versionText, out nextVersion))
                return;
            Version previous = Path.IsPathRooted(target) ? PortableUpgrade.ReadVersion(target) : null;
            if (Path.GetFileName(helper) != HelperName || previous == null ||
                previous != typeof(ReleaseUpdates).Assembly.GetName().Version || nextVersion <= previous)
                return;
            string directory = Path.GetDirectoryName(helper);
            try
            {
                if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) return;
                string fullDir = Path.GetFullPath(directory);
                if (!fullDir.EndsWith(Path.DirectorySeparatorChar.ToString())) fullDir += Path.DirectorySeparatorChar;
                string fullFile = Path.GetFullPath(file);
                if (!fullFile.StartsWith(fullDir, StringComparison.OrdinalIgnoreCase) || !File.Exists(fullFile)) return;
                if (PortableUpgrade.ReadVersion(fullFile) != nextVersion) return;
                string rootDir = Path.GetDirectoryName(Path.GetDirectoryName(helper));
                string updatedMark = Path.Combine(rootDir, UpdatedMarkerName);
                PortableUpgrade.ApplyVerified(fullFile, target, previous, delegate
                {
                    try { File.WriteAllText(updatedMark, nextVersion.ToString()); }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                });
            }
            catch { }
            finally { DeleteDownload(file); }
        }

        internal void Install()
        {
            ReleaseAsset selected;
            lock (gate)
            {
                if (state.Busy || asset == null) return;
                selected = asset;
                state = new UpdateStatus(UpdatePhase.Downloading, selected.Version);
            }
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    Directory.CreateDirectory(cache);
                    if ((File.GetAttributes(cache) & FileAttributes.ReparsePoint) != 0)
                        throw new IOException("The update cache is a redirected directory.");
                    string helper = Path.Combine(cache, HelperName);
                    File.Copy(executable, helper, true);
                    using (Process next = Process.Start(new ProcessStartInfo(helper, "--update-window \"" + executable + "\"")
                        { WorkingDirectory = cache, UseShellExecute = false }))
                    {
                        if (next == null) throw new IOException("Could not start the update.");
                        next.WaitForExit();
                        // A successful handoff has already closed this process.
                        throw new IOException("The update closed before the running copy was replaced.");
                    }
                }
                catch (Exception error)
                {
                    Report(error);
                    lock (gate) state = new UpdateStatus(UpdatePhase.InstallFailed, selected.Version);
                }
            });
        }

        private void Report(Exception error)
        {
            if (log != null) log("GitHub 更新：" + error.Message);
        }

        internal static void DeleteDownload(string path)
        {
            try { File.Delete(path); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        internal static void RunWindow(string helper, string target)
        {
            Version previous = Path.IsPathRooted(target) ? PortableUpgrade.ReadVersion(target) : null;
            if (Path.GetFileName(helper) != HelperName || previous != typeof(ReleaseUpdates).Assembly.GetName().Version) return;
            string directory = Path.GetDirectoryName(helper);
            AppSettings settings = AppSettings.Load(Path.GetDirectoryName(directory));
            L.AutoDetect();
            if (settings.language != "auto") L.Lang = settings.language;
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            UiTheme.Set(settings.theme);
            using (var restart = new ManualResetEvent(false))
            using (var dialog = new ReleaseUpdateDialog())
            {
                dialog.Shown += delegate
                {
                    ThreadPool.QueueUserWorkItem(delegate
                    {
                        string file = null;
                        try
                        {
                            ReleaseAsset selected = CheckLatest(previous);
                            if (selected == null) throw new IOException(L.T("未找到可以应用的更新。"));
                            file = Path.Combine(directory, "CodexQuotaTray-" + selected.Version + ".exe");
                            DeleteDownload(file);
                            dialog.Post(delegate { dialog.SetDownloading(selected.Version, 0); });
                            Download(selected, file, delegate(int percent)
                            {
                                dialog.Post(delegate { dialog.SetDownloading(selected.Version, percent); });
                            });
                            dialog.Post(dialog.SetInstalling);
                            PortableUpgrade.ApplyVerified(file, target, previous, delegate
                            {
                                dialog.Post(delegate { dialog.SetComplete(selected.Version, delegate { restart.Set(); }); });
                                restart.WaitOne();
                            });
                            dialog.Post(dialog.Finish);
                        }
                        catch (Exception error)
                        {
                            dialog.Post(delegate { dialog.SetFailure(error.Message); });
                        }
                        finally { if (file != null) DeleteDownload(file); }
                    });
                };
                Application.Run(dialog);
            }
        }

        internal static ReleaseAsset CheckLatest(Version current)
        {
            Uri url = new Uri("https://api.github.com/repos/" + Repository + "/releases/latest");
            HttpWebRequest request = Request(url);
            request.Accept = "application/vnd.github+json";
            using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
            using (Stream stream = response.GetResponseStream())
            using (var json = new MemoryStream())
            {
                CopyBounded(stream, json, MaxMetadataSize);
                return ParseRelease(Encoding.UTF8.GetString(json.ToArray()), current);
            }
        }

        internal static ReleaseAsset ParseRelease(string json, Version current)
        {
            ReleaseDto release = new JavaScriptSerializer().Deserialize<ReleaseDto>(json);
            Version version;
            if (release == null || release.draft || release.prerelease ||
                String.IsNullOrEmpty(release.tag_name) || release.tag_name[0] != 'v' ||
                !Version.TryParse(release.tag_name.Substring(1), out version) || version.Revision < 0 ||
                release.tag_name != "v" + version)
                throw new InvalidDataException("The public release has an unexpected version tag.");
            if (version <= current) return null;

            string name = "CodexQuotaTray-" + version + ".exe";
            Uri expected = new Uri("https://github.com/" + Repository + "/releases/download/" + release.tag_name + "/" + name);
            foreach (AssetDto file in release.assets ?? new AssetDto[0])
            {
                if (file == null || file.name != name) continue;
                if (file.state != "uploaded" || file.size <= 0 || file.size > MaxAssetSize ||
                    !String.Equals(file.browser_download_url, expected.AbsoluteUri, StringComparison.OrdinalIgnoreCase) ||
                    file.digest == null || !Regex.IsMatch(file.digest, @"^sha256:[0-9a-fA-F]{64}$", RegexOptions.CultureInvariant))
                    break;
                return new ReleaseAsset { Version = version, Url = expected, Size = file.size, Digest = file.digest.Substring(7) };
            }
            throw new InvalidDataException("The public release does not contain a verifiable EXE.");
        }

        internal static void Download(ReleaseAsset selected, string path, Action<int> progress = null)
        {
            using (HttpWebResponse response = OpenDownload(selected.Url))
            using (Stream source = response.GetResponseStream())
            using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
            {
                if (response.ContentLength >= 0 && response.ContentLength != selected.Size)
                    throw new InvalidDataException("The release download size does not match.");
                int previous = -1;
                CopyBounded(source, file, selected.Size, delegate(long bytes)
                {
                    int percent = (int)Math.Min(99, bytes * 100 / selected.Size);
                    if (progress == null || percent == previous) return;
                    previous = percent;
                    progress(percent);
                });
                file.Position = 0;
                VerifyAsset(file, selected);
            }
            if (PortableUpgrade.ReadVersion(path) != selected.Version)
                throw new InvalidDataException("The downloaded EXE version does not match the release.");
            if (progress != null) progress(100);
        }

        internal static void VerifyAsset(Stream file, ReleaseAsset selected)
        {
            if (file.Length != selected.Size) throw new InvalidDataException("The release download is incomplete.");
            using (var sha = SHA256.Create())
            {
                string actual = BitConverter.ToString(sha.ComputeHash(file)).Replace("-", "");
                if (!String.Equals(actual, selected.Digest, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("The release download failed its SHA-256 check.");
            }
        }

        private static HttpWebResponse OpenDownload(Uri url)
        {
            for (int hop = 0; hop < 5; hop++)
            {
                var response = (HttpWebResponse)Request(url).GetResponse();
                if (response.StatusCode == HttpStatusCode.OK) return response;
                using (response)
                {
                    int status = (int)response.StatusCode;
                    Uri next;
                    if ((status != 301 && status != 302 && status != 303 && status != 307 && status != 308) ||
                        !Uri.TryCreate(url, response.Headers[HttpResponseHeader.Location], out next) ||
                        !IsAssetHost(next))
                        throw new IOException("Unexpected release download redirect.");
                    url = next;
                }
            }
            throw new IOException("Too many release download redirects.");
        }

        internal static bool IsAssetHost(Uri url)
        {
            return url.Scheme == Uri.UriSchemeHttps && url.IsDefaultPort && String.IsNullOrEmpty(url.UserInfo) &&
                (url.Host == "release-assets.githubusercontent.com" || url.Host == "objects.githubusercontent.com");
        }

        private static HttpWebRequest Request(Uri url)
        {
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; // TLS 1.2 under .NET Framework 4
            var request = (HttpWebRequest)WebRequest.Create(url);
            request.UserAgent = "CodexQuotaTray";
            request.Timeout = 15000;
            request.ReadWriteTimeout = 15000;
            request.AllowAutoRedirect = false;
            return request;
        }

        private static void CopyBounded(Stream source, Stream target, long max, Action<long> progress = null)
        {
            var buffer = new byte[32768];
            long total = 0;
            for (int bytes; (bytes = source.Read(buffer, 0, buffer.Length)) > 0;)
            {
                total += bytes;
                if (total > max) throw new InvalidDataException("The release response exceeded its expected size.");
                target.Write(buffer, 0, bytes);
                if (progress != null) progress(total);
            }
        }

        private sealed class ReleaseDto
        {
            public string tag_name { get; set; }
            public bool draft { get; set; }
            public bool prerelease { get; set; }
            public AssetDto[] assets { get; set; }
        }

        private sealed class AssetDto
        {
            public string name { get; set; }
            public string state { get; set; }
            public string browser_download_url { get; set; }
            public string digest { get; set; }
            public long size { get; set; }
        }
    }
}
