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
}
