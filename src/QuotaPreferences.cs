using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace QuotaTrayApp
{
    // 服务端历史标识只在入口兼容，界面统一显示当前套餐名称。
    internal static class PlanNames
    {
        private static string Clean(string value)
        {
            return (value ?? "").Trim().ToLowerInvariant().Replace(" ", "");
        }
        internal static string Normalize(string value)
        {
            string p = Clean(value);
            switch (p)
            {
                case "prolite": case "pro100": case "pro_100": return "pro_100";
                case "pro": case "pro200": case "pro_200": return "pro_200";
                case "promax": case "pro500": case "pro_500": return "pro_500";
                case "free": case "go": case "plus": case "team": case "business": case "enterprise": case "edu": return p;
                default: return "unknown";
            }
        }
        private static bool IsToken(string value)
        {
            if (String.IsNullOrEmpty(value) || value.Length > 32) return false;
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (!((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_')) return false;
            }
            return true;
        }
        // 落库值：认识的套餐用规范名；没见过的合法串原样留着，
        // 这样升/降级到新命名的套餐时不会一直显示旧套餐名。
        internal static string Store(string value)
        {
            string p = Clean(value);
            string known = Normalize(p);
            if (known != "unknown") return known;
            return IsToken(p) ? p : "unknown";
        }
        internal static string Title(string value)
        {
            switch (Normalize(value))
            {
                case "pro_100": return "Pro100";
                case "pro_200": return "Pro200";
                case "pro_500": return "Pro500";
                case "plus": return "Plus";
                case "free": return "Free";
                case "go": return "Go";
                case "team": return "Team";
                case "business": return "Business";
                case "enterprise": return "Enterprise";
                case "edu": return "Edu";
            }
            string stored = Store(value);
            if (stored == "unknown") return "—";
            return Char.ToUpperInvariant(stored[0]) + stored.Substring(1);
        }
    }

    // 额度窗口的长短判定：一天及以上算长期窗口（7 天、30 天、日额度都归这一类），
    // 其余按小时显示。以前只认「大于 1440」，恰好 1 天的窗口会被当成 5 小时。
    internal static class QuotaWindow
    {
        internal const int DayMinutes = 1440;
        internal static bool IsLong(double windowMins) { return windowMins >= DayMinutes; }
        internal static int Days(double windowMins) { return Math.Max(1, (int)Math.Round(windowMins / (double)DayMinutes)); }
        internal static int DisplayUsed(double used) { return Math.Max(0, Math.Min(100, (int)Math.Round(used))); }
        internal static int Remaining(double used) { return 100 - DisplayUsed(used); }
    }

    internal sealed class DailyPaceDecision
    {
        internal bool Triggered;
        internal bool StateChanged;
        internal int RemainingDays;
        internal double DayStartRemaining;
        internal double CurrentRemaining;
        internal double TodayUsed;
        internal double DailyBudget;
        internal string Reason;
    }

    internal sealed class DailyPaceSlotState
    {
        internal double CycleResetsAt;
        internal string DayKey = "";
        internal double DayStartRemaining;
        internal string NotifiedDayKey = "";
    }

    // 7 天（及更长）周期的「推荐用量提醒」动态节奏追踪。
    // Plus 与 Pro 共用同一套标准；耗尽（0%）及最后不足 24 小时不触发，每日每账号最多提醒一次。
    internal sealed class DailyPaceTracker
    {
        private const double CycleResetToleranceSeconds = 60.0;
        private const double SecondsPerDay = 86400.0;
        private readonly Dictionary<string, DailyPaceSlotState> states = new Dictionary<string, DailyPaceSlotState>(StringComparer.Ordinal);
        private string storageRoot;

        internal DailyPaceSlotState SnapshotState(string slot)
        {
            DailyPaceSlotState state;
            if (!states.TryGetValue(SafeSlot(slot), out state)) return null;
            return new DailyPaceSlotState
            {
                CycleResetsAt = state.CycleResetsAt,
                DayKey = state.DayKey,
                DayStartRemaining = state.DayStartRemaining,
                NotifiedDayKey = state.NotifiedDayKey
            };
        }

        internal void SeedState(string slot, double cycleResetsAt, string dayKey, double dayStartRemaining, string notifiedDayKey)
        {
            states[SafeSlot(slot)] = new DailyPaceSlotState
            {
                CycleResetsAt = cycleResetsAt,
                DayKey = dayKey ?? "",
                DayStartRemaining = Math.Max(0.0, Math.Min(100.0, dayStartRemaining)),
                NotifiedDayKey = notifiedDayKey ?? ""
            };
            SaveIfConfigured();
        }

        internal void ResetSlot(string slot)
        {
            if (states.Remove(SafeSlot(slot))) SaveIfConfigured();
        }

        internal void Clear()
        {
            if (states.Count == 0) return;
            states.Clear();
            SaveIfConfigured();
        }

        internal DailyPaceDecision Evaluate(string slot, bool enabled, double used, double resetsAt, DateTimeOffset nowUtc)
        {
            string safeSlot = SafeSlot(slot);
            double remaining = Math.Max(0.0, Math.Min(100.0, 100.0 - used));
            var decision = new DailyPaceDecision { CurrentRemaining = remaining };

            if (double.IsNaN(used) || double.IsInfinity(used) || used < 0.0 || used > 100.0 ||
                double.IsNaN(resetsAt) || double.IsInfinity(resetsAt) || resetsAt <= 0.0)
            {
                decision.Reason = "invalid";
                return decision;
            }

            double nowUnix = nowUtc.ToUnixTimeSeconds();
            double secondsLeft = resetsAt - nowUnix;
            string dayKey = nowUtc.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

            DailyPaceSlotState state;
            if (!states.TryGetValue(safeSlot, out state))
            {
                state = new DailyPaceSlotState();
                states[safeSlot] = state;
            }

            bool newCycle = state.CycleResetsAt <= 0.0 || Math.Abs(resetsAt - state.CycleResetsAt) > CycleResetToleranceSeconds;
            if (newCycle || state.DayKey != dayKey)
            {
                state.CycleResetsAt = resetsAt;
                state.DayKey = dayKey;
                state.DayStartRemaining = remaining;
                state.NotifiedDayKey = "";
                decision.StateChanged = true;
            }
            else if (remaining > state.DayStartRemaining)
            {
                state.DayStartRemaining = remaining;
                decision.StateChanged = true;
            }

            decision.DayStartRemaining = state.DayStartRemaining;
            decision.TodayUsed = Math.Max(0.0, state.DayStartRemaining - remaining);

            if (!enabled)
            {
                decision.Reason = "disabled";
                if (decision.StateChanged) SaveIfConfigured();
                return decision;
            }

            if (QuotaWindow.Remaining(used) <= 0 || remaining <= 0.0)
            {
                decision.Reason = "exhausted";
                if (decision.StateChanged) SaveIfConfigured();
                return decision;
            }

            if (secondsLeft <= 0.0)
            {
                decision.Reason = "invalid";
                if (decision.StateChanged) SaveIfConfigured();
                return decision;
            }

            if (secondsLeft <= SecondsPerDay)
            {
                decision.RemainingDays = 1;
                decision.DailyBudget = remaining;
                decision.Reason = "final_day";
                if (decision.StateChanged) SaveIfConfigured();
                return decision;
            }

            int remainingDays = Math.Max(2, (int)Math.Ceiling(secondsLeft / SecondsPerDay));
            double dailyBudget = state.DayStartRemaining / (double)remainingDays;
            decision.RemainingDays = remainingDays;
            decision.DailyBudget = dailyBudget;

            if (state.NotifiedDayKey == dayKey)
            {
                decision.Reason = "already_notified";
                if (decision.StateChanged) SaveIfConfigured();
                return decision;
            }

            if (dailyBudget > 0.0 && decision.TodayUsed + 1e-6 >= dailyBudget)
            {
                state.NotifiedDayKey = dayKey;
                decision.Triggered = true;
                decision.StateChanged = true;
                decision.Reason = "triggered";
                SaveIfConfigured();
                return decision;
            }

            decision.Reason = "below_budget";
            if (decision.StateChanged) SaveIfConfigured();
            return decision;
        }

        internal static DailyPaceTracker Load(string root)
        {
            var tracker = new DailyPaceTracker { storageRoot = root };
            if (string.IsNullOrEmpty(root)) return tracker;
            try
            {
                string path = Path.Combine(root, "daily-pace-state.txt");
                if (!File.Exists(path)) return tracker;
                foreach (string raw in File.ReadAllLines(path, Encoding.UTF8))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#")) continue;
                    string[] parts = line.Split('|');
                    if (parts.Length < 5) continue;
                    double resetsAt, dayStart;
                    if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out resetsAt) ||
                        !double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out dayStart))
                        continue;
                    tracker.states[SafeSlot(parts[0])] = new DailyPaceSlotState
                    {
                        CycleResetsAt = resetsAt,
                        DayKey = parts[2],
                        DayStartRemaining = Math.Max(0.0, Math.Min(100.0, dayStart)),
                        NotifiedDayKey = parts[4]
                    };
                }
            }
            catch { }
            return tracker;
        }

        internal bool Save(string root)
        {
            if (string.IsNullOrEmpty(root)) return false;
            string temp = null;
            try
            {
                Directory.CreateDirectory(root);
                var sb = new StringBuilder();
                var keys = new List<string>(states.Keys);
                keys.Sort(StringComparer.Ordinal);
                foreach (string key in keys)
                {
                    DailyPaceSlotState s = states[key];
                    sb.Append(key).Append('|')
                      .Append(s.CycleResetsAt.ToString("R", CultureInfo.InvariantCulture)).Append('|')
                      .Append(SanitizeField(s.DayKey)).Append('|')
                      .Append(s.DayStartRemaining.ToString("R", CultureInfo.InvariantCulture)).Append('|')
                      .Append(SanitizeField(s.NotifiedDayKey)).AppendLine();
                }
                string target = Path.Combine(root, "daily-pace-state.txt");
                temp = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
                File.WriteAllText(temp, sb.ToString(), new UTF8Encoding(false));
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

        private void SaveIfConfigured()
        {
            if (!string.IsNullOrEmpty(storageRoot)) Save(storageRoot);
        }

        private static string SafeSlot(string slot) { return string.IsNullOrEmpty(slot) ? "single" : slot; }
        private static string SanitizeField(string value) { return string.IsNullOrEmpty(value) ? "" : value.Replace("|", "").Replace("\r", "").Replace("\n", ""); }
    }

    internal sealed class ResetCreditDecision
    {
        internal bool Initialized;
        internal bool StateChanged;
        internal bool NewCreditTriggered;
        internal bool ExpiringTriggered;
        internal int AvailableCount;
        internal int NewCreditCount;
        internal int ExpiringCount;
        internal double SoonestExpiresAt;
        internal double ExpiringSoonestAt;
        internal string Reason = "";
    }

    internal sealed class ResetCreditSlotState
    {
        internal bool Initialized;
        internal int LastKnownCount;
        internal readonly HashSet<string> KnownIds = new HashSet<string>(StringComparer.Ordinal);
        internal readonly HashSet<string> ExpiringNotifiedIds = new HashSet<string>(StringComparer.Ordinal);
    }

    // 仅只读跟踪重置券张数与过期时间，绝不自动核销；首轮静默建基线，新券或 24 小时内临期才提醒。
    internal sealed class ResetCreditTracker
    {
        internal const double ExpiringSoonSeconds = 24.0 * 3600.0;
        internal const int DetailIntervalMinutes = 15;

        private readonly Dictionary<string, ResetCreditSlotState> states =
            new Dictionary<string, ResetCreditSlotState>(StringComparer.Ordinal);
        private string storageRoot;

        internal static double SoonestExpiry(ResetCreditsInfo info, DateTimeOffset nowUtc)
        {
            if (info == null || !info.hasDetails || info.credits == null) return 0.0;
            double nowUnix = nowUtc.ToUnixTimeSeconds();
            double soonest = 0.0;
            foreach (ResetCreditItem item in info.credits)
            {
                if (!IsActiveItem(item, nowUnix) || item.expiresAt <= nowUnix) continue;
                if (soonest <= 0.0 || item.expiresAt < soonest) soonest = item.expiresAt;
            }
            return soonest;
        }

        private static bool IsActiveItem(ResetCreditItem item, double nowUnix)
        {
            if (item == null || string.IsNullOrEmpty(item.id)) return false;
            if (!string.IsNullOrEmpty(item.status) && item.status != "available") return false;
            if (item.expiresAt > 0.0 && item.expiresAt <= nowUnix) return false;
            return true;
        }

        internal void ResetSlot(string slot)
        {
            string key = SafeSlot(slot);
            if (!states.Remove(key)) return;
            SaveIfConfigured();
        }

        internal void Clear()
        {
            if (states.Count == 0) return;
            states.Clear();
            SaveIfConfigured();
        }

        internal ResetCreditDecision Evaluate(string slot, bool enabled, ResetCreditsInfo info, DateTimeOffset nowUtc, bool allowCountOnly = false)
        {
            string key = SafeSlot(slot);
            var decision = new ResetCreditDecision();
            if (info == null || info.availableCount < 0)
            {
                decision.Reason = "unavailable";
                return decision;
            }

            double nowUnix = nowUtc.ToUnixTimeSeconds();
            double soonest = SoonestExpiry(info, nowUtc);
            decision.AvailableCount = info.availableCount;
            decision.SoonestExpiresAt = soonest;

            ResetCreditSlotState state;
            if (!states.TryGetValue(key, out state))
            {
                state = new ResetCreditSlotState();
                states[key] = state;
            }

            var activeItems = new List<ResetCreditItem>();
            var activeIdSet = new HashSet<string>(StringComparer.Ordinal);
            if (info.hasDetails && info.credits != null)
            {
                foreach (ResetCreditItem item in info.credits)
                {
                    if (!IsActiveItem(item, nowUnix)) continue;
                    if (activeIdSet.Add(item.id)) activeItems.Add(item);
                }
            }

            if (!state.Initialized)
            {
                state.Initialized = true;
                state.LastKnownCount = info.availableCount;
                if (info.hasDetails)
                    foreach (string id in activeIdSet) state.KnownIds.Add(id);
                decision.Initialized = true;
                decision.StateChanged = true;
                CheckExpiring(state, activeItems, activeIdSet, enabled, nowUnix, decision, allowTrigger: true);
                if (decision.Reason.Length == 0) decision.Reason = "baseline";
                SaveIfConfigured();
                return decision;
            }

            decision.Initialized = true;
            if (info.hasDetails)
            {
                int newCount = 0;
                foreach (string id in activeIdSet)
                    if (!state.KnownIds.Contains(id)) newCount++;
                if (!state.KnownIds.SetEquals(activeIdSet) || state.LastKnownCount != info.availableCount)
                {
                    state.KnownIds.Clear();
                    foreach (string id in activeIdSet) state.KnownIds.Add(id);
                    state.LastKnownCount = info.availableCount;
                    decision.StateChanged = true;
                }
                if (newCount > 0 && enabled)
                {
                    decision.NewCreditTriggered = true;
                    decision.NewCreditCount = newCount;
                    decision.Reason = "new_credit";
                }
                CheckExpiring(state, activeItems, activeIdSet, enabled, nowUnix, decision, allowTrigger: !decision.NewCreditTriggered);
            }
            else
            {
                if (info.availableCount == 0 && (state.LastKnownCount != 0 || state.KnownIds.Count > 0 || state.ExpiringNotifiedIds.Count > 0))
                {
                    state.LastKnownCount = 0;
                    state.KnownIds.Clear();
                    state.ExpiringNotifiedIds.Clear();
                    decision.StateChanged = true;
                }
                else if (allowCountOnly && info.availableCount > state.LastKnownCount)
                {
                    int diff = info.availableCount - state.LastKnownCount;
                    state.LastKnownCount = info.availableCount;
                    decision.StateChanged = true;
                    if (enabled)
                    {
                        decision.NewCreditTriggered = true;
                        decision.NewCreditCount = diff;
                        decision.Reason = "new_credit_count";
                    }
                }
            }

            if (decision.Reason.Length == 0) decision.Reason = enabled ? "unchanged" : "disabled";
            if (decision.StateChanged) SaveIfConfigured();
            return decision;
        }

        private static void CheckExpiring(ResetCreditSlotState state, List<ResetCreditItem> activeItems, HashSet<string> activeIdSet,
            bool enabled, double nowUnix, ResetCreditDecision decision, bool allowTrigger)
        {
            var stale = new List<string>();
            foreach (string oldId in state.ExpiringNotifiedIds)
                if (!activeIdSet.Contains(oldId)) stale.Add(oldId);
            if (stale.Count > 0)
            {
                foreach (string id in stale) state.ExpiringNotifiedIds.Remove(id);
                decision.StateChanged = true;
            }
            if (!enabled) return;

            int expCount = 0;
            double expSoonest = 0.0;
            foreach (ResetCreditItem item in activeItems)
            {
                double left = item.expiresAt - nowUnix;
                if (item.expiresAt <= 0.0 || left <= 0.0 || left > ExpiringSoonSeconds) continue;
                if (state.ExpiringNotifiedIds.Add(item.id))
                {
                    decision.StateChanged = true;
                    expCount++;
                    if (expSoonest <= 0.0 || item.expiresAt < expSoonest) expSoonest = item.expiresAt;
                }
            }
            if (allowTrigger && expCount > 0)
            {
                decision.ExpiringTriggered = true;
                decision.ExpiringCount = expCount;
                decision.ExpiringSoonestAt = expSoonest;
                decision.Reason = "expiring_soon";
            }
        }

        internal static ResetCreditTracker Load(string root)
        {
            var tracker = new ResetCreditTracker { storageRoot = root };
            if (string.IsNullOrEmpty(root)) return tracker;
            try
            {
                string path = Path.Combine(root, "reset-credits-state.txt");
                if (!File.Exists(path)) return tracker;
                foreach (string raw in File.ReadAllLines(path, Encoding.UTF8))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#")) continue;
                    string[] parts = line.Split('|');
                    if (parts.Length < 5) continue;
                    int count;
                    if (!int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out count)) count = 0;
                    var state = new ResetCreditSlotState
                    {
                        Initialized = parts[1] == "1",
                        LastKnownCount = Math.Max(0, count)
                    };
                    AddIds(state.KnownIds, parts[3]);
                    AddIds(state.ExpiringNotifiedIds, parts[4]);
                    tracker.states[SafeSlot(parts[0])] = state;
                }
            }
            catch { }
            return tracker;
        }

        internal bool Save(string root)
        {
            if (string.IsNullOrEmpty(root)) return false;
            string temp = null;
            try
            {
                Directory.CreateDirectory(root);
                var sb = new StringBuilder();
                var keys = new List<string>(states.Keys);
                keys.Sort(StringComparer.Ordinal);
                foreach (string key in keys)
                {
                    ResetCreditSlotState s = states[key];
                    sb.Append(key).Append('|')
                      .Append(s.Initialized ? "1" : "0").Append('|')
                      .Append(s.LastKnownCount.ToString(CultureInfo.InvariantCulture)).Append('|')
                      .Append(JoinIds(s.KnownIds)).Append('|')
                      .Append(JoinIds(s.ExpiringNotifiedIds)).AppendLine();
                }
                string target = Path.Combine(root, "reset-credits-state.txt");
                temp = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
                File.WriteAllText(temp, sb.ToString(), new UTF8Encoding(false));
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

        private void SaveIfConfigured()
        {
            if (!string.IsNullOrEmpty(storageRoot)) Save(storageRoot);
        }

        private static string SafeSlot(string slot) { return string.IsNullOrEmpty(slot) ? "single" : slot; }
        private static string SanitizeId(string id) { return string.IsNullOrEmpty(id) ? "" : id.Replace("|", "_").Replace(",", "_").Replace("\r", "").Replace("\n", ""); }
        private static string JoinIds(HashSet<string> ids)
        {
            if (ids == null || ids.Count == 0) return "";
            var list = new List<string>();
            foreach (string id in ids) { string s = SanitizeId(id); if (s.Length > 0) list.Add(s); }
            list.Sort(StringComparer.Ordinal);
            return string.Join(",", list.ToArray());
        }
        private static void AddIds(HashSet<string> dest, string csv)
        {
            if (string.IsNullOrEmpty(csv)) return;
            foreach (string raw in csv.Split(',')) { string id = raw.Trim(); if (id.Length > 0) dest.Add(id); }
        }
    }
}
