using System;
namespace QuotaTrayApp
{
    // 服务端历史标识只在入口兼容，界面统一显示当前套餐名称。
    internal static class PlanNames
    {
        internal static string Normalize(string value)
        {
            string p = (value ?? "").Trim().ToLowerInvariant().Replace(" ", "");
            switch (p)
            {
                case "prolite": case "pro100": case "pro_100": return "pro_100";
                case "pro": case "pro200": case "pro_200": return "pro_200";
                case "promax": case "pro500": case "pro_500": return "pro_500";
                case "free": case "go": case "plus": case "team": case "business": case "enterprise": case "edu": return p;
                default: return "unknown";
            }
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
                default: return "—";
            }
        }
    }
}
