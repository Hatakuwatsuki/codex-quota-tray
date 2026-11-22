using System;
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
}
