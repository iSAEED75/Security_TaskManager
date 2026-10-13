using ClosedXML.Excel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using TasksWebApp.Data;
using TasksWebApp.Helpers;
using TasksWebApp.Models;
using TaskStatus = TasksWebApp.Models.TaskStatus;

namespace TasksWebApp.Pages.Reports;

[Authorize(Roles = "Admin")]
public class IndexModel(AppDbContext db) : PageModel
{
    [BindProperty(SupportsGet = true)] public string? FromShamsi { get; set; }
    [BindProperty(SupportsGet = true)] public string? ToShamsi { get; set; }
    public DateTime? From { get; private set; }
    public DateTime? To { get; private set; }

    private void ParseDateFilters()
    {
        From = string.IsNullOrWhiteSpace(FromShamsi) ? null :
            PersianDateHelper.TryParse(FromShamsi, out var fromDate) ? fromDate.Date : null;
        To = string.IsNullOrWhiteSpace(ToShamsi) ? null :
            PersianDateHelper.TryParse(ToShamsi, out var toDate) ? toDate.Date : null;
    }
    public int Total { get; set; }
    public decimal TotalHours { get; set; }
    public Dictionary<string, int> StatusCounts { get; set; } = [];
    public Dictionary<string, int> PriorityCounts { get; set; } = [];
    public List<(string Name, int Count)> CategoryCounts { get; set; } = [];

    private IQueryable<TaskItem> Query() => db.TaskItems.AsNoTracking()
        .Where(x => !x.IsDeleted && (!From.HasValue || x.WorkDate >= From.Value.Date) && (!To.HasValue || x.WorkDate < To.Value.Date.AddDays(1)));

    public async Task OnGetAsync()
    {
        ParseDateFilters();
        var q = Query();
        Total = await q.CountAsync();
        TotalHours = await q.SumAsync(x => (decimal?)x.DurationHours) ?? 0m;
        var statusLabels = new Dictionary<TaskStatus, string> { [TaskStatus.Pending] = "ثبت‌شده", [TaskStatus.InProgress] = "در حال پیگیری", [TaskStatus.Completed] = "پایان‌یافته" };
        foreach (TaskStatus status in Enum.GetValues<TaskStatus>()) StatusCounts[statusLabels[status]] = await q.CountAsync(x => x.Status == status);
        var priorityLabels = new Dictionary<TaskPriority, string> { [TaskPriority.Low] = "کم", [TaskPriority.Medium] = "معمولی", [TaskPriority.High] = "زیاد", [TaskPriority.Critical] = "بسیار مهم" };
        foreach (TaskPriority priority in Enum.GetValues<TaskPriority>()) PriorityCounts[priorityLabels[priority]] = await q.CountAsync(x => x.Priority == priority);
        var grouped = await q.GroupBy(x => x.Category == null ? "بدون دسته‌بندی" : x.Category.Name)
            .Select(g => new { Name = g.Key, Count = g.Count() }).ToListAsync();
        CategoryCounts = grouped.Select(x => (x.Name, x.Count)).OrderByDescending(x => x.Count).ToList();
    }

    public async Task<IActionResult> OnGetExportToExcelAsync()
    {
        ParseDateFilters();
        var rows = await Query().Include(x => x.Category).Include(x => x.CreatedBy)
            .OrderByDescending(x => x.WorkDate).ThenByDescending(x => x.CreatedAt).ToListAsync();
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("گزارش کار");
        string[] headers = { "تاریخ شمسی", "نوع ثبت", "عنوان", "شرح فعالیت یا وظیفه", "مدت / زمان (ساعت)", "وضعیت", "اولویت", "دسته‌بندی", "ثبت‌کننده", "تاریخ ثبت در سامانه" };
        for (int i = 0; i < headers.Length; i++) ws.Cell(1, i + 1).Value = headers[i];
        string StatusLabel(TaskStatus value) => value switch { TaskStatus.Pending => "ثبت‌شده", TaskStatus.InProgress => "در حال پیگیری", TaskStatus.Completed => "پایان‌یافته", _ => "نامشخص" };
        string PriorityLabel(TaskPriority value) => value switch { TaskPriority.Low => "کم", TaskPriority.Medium => "معمولی", TaskPriority.High => "زیاد", TaskPriority.Critical => "بسیار مهم", _ => "نامشخص" };
        for (int i = 0; i < rows.Count; i++)
        {
            var x = rows[i]; var n = i + 2;
            ws.Cell(n, 1).Value = PersianDateHelper.Format(x.WorkDate);
            ws.Cell(n, 2).Value = x.RecordType == TaskRecordType.Todo ? "وظیفه" : "گزارش کار";
            ws.Cell(n, 3).Value = x.Title; ws.Cell(n, 4).Value = x.Description ?? "";
            ws.Cell(n, 5).Value = x.DurationHours; ws.Cell(n, 6).Value = StatusLabel(x.Status);
            ws.Cell(n, 7).Value = PriorityLabel(x.Priority); ws.Cell(n, 8).Value = x.Category?.Name ?? "بدون دسته‌بندی";
            ws.Cell(n, 9).Value = x.CreatedBy.FullName; ws.Cell(n, 10).Value = x.CreatedAt;
        }
        ws.RightToLeft = true; ws.Row(1).Style.Font.Bold = true; ws.Row(1).Style.Alignment.WrapText = true;
        ws.SheetView.FreezeRows(1); ws.Columns().AdjustToContents();
        using var ms = new MemoryStream(); wb.SaveAs(ms);
        return File(ms.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "گزارش-کار.xlsx");
    }
}
