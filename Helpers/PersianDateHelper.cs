using System.Text;

namespace TasksWebApp.Helpers;

public static class PersianDateHelper
{
    private static readonly System.Globalization.PersianCalendar Calendar = new();

    public static bool TryParse(string? value, out DateTime date)
    {
        date = default;
        if (string.IsNullOrWhiteSpace(value)) return false;
        var normalized = NormalizeDigits(value.Trim()).Replace('-', '/').Replace('.', '/');
        var parts = normalized.Split('/');
        if (parts.Length != 3 ||
            !int.TryParse(parts[0], out var year) ||
            !int.TryParse(parts[1], out var month) ||
            !int.TryParse(parts[2], out var day))
            return false;
        try
        {
            date = Calendar.ToDateTime(year, month, day, 0, 0, 0, 0);
            return true;
        }
        catch (ArgumentOutOfRangeException) { return false; }
    }

    public static string Format(DateTime date) =>
        $"{Calendar.GetYear(date):0000}/{Calendar.GetMonth(date):00}/{Calendar.GetDayOfMonth(date):00}";

    private static string NormalizeDigits(string value)
    {
        var sb = new StringBuilder(value.Length);
        foreach (var ch in value)
            sb.Append(ch switch
            {
                >= '۰' and <= '۹' => (char)('0' + ch - '۰'),
                >= '٠' and <= '٩' => (char)('0' + ch - '٠'),
                _ => ch
            });
        return sb.ToString();
    }
}
