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
public class PerformanceModel(AppDbContext db) : PageModel
{
    [BindProperty(SupportsGet = true)] public string? FromShamsi { get; set; }
    [BindProperty(SupportsGet = true)] public string? ToShamsi { get; set; }
    public List<EmployeeStat> Stats { get; private set; } = [];
    public record EmployeeStat(string Name, int Assigned, int Completed, int Overdue, decimal Hours, int Reports);

    private IQueryable<TaskItem> Filtered()
    {
        var q = db.TaskItems.AsNoTracking().Where(x => !x.IsDeleted);
        if (PersianDateHelper.TryParse(FromShamsi, out var from)) q = q.Where(x => x.WorkDate >= from.Date);
        if (PersianDateHelper.TryParse(ToShamsi, out var to)) q = q.Where(x => x.WorkDate < to.Date.AddDays(1));
        return q;
    }
    public async Task OnGetAsync()
    {
        var users = await db.Users.AsNoTracking().OrderBy(x => x.FullName).ToListAsync();
        var q = Filtered();
        foreach (var user in users)
        {
            var mine = q.Where(x => x.AssignedToId == user.Id || (x.AssignedToId == null && x.CreatedById == user.Id));
            var assigned = await mine.CountAsync(x => x.RecordType == TaskRecordType.Todo);
            var completed = await mine.CountAsync(x => x.RecordType == TaskRecordType.Todo && x.Status == TaskStatus.Completed);
            var overdue = await mine.CountAsync(x => x.RecordType == TaskRecordType.Todo && x.Status != TaskStatus.Completed && x.WorkDate < DateTime.Today);
            var reports = await mine.CountAsync(x => x.RecordType == TaskRecordType.WorkReport);
            var hours = await mine.Where(x => x.RecordType == TaskRecordType.WorkReport || x.Status == TaskStatus.Completed).SumAsync(x => (decimal?)x.DurationHours) ?? 0m;
            Stats.Add(new EmployeeStat(user.FullName, assigned, completed, overdue, hours, reports));
        }
    }
    public async Task<IActionResult> OnGetExportAsync()
    {
        await OnGetAsync();
        using var wb = new XLWorkbook(); var ws = wb.Worksheets.Add("عملکرد کارکنان");
        string[] headers = ["کارمند", "وظایف واگذارشده", "وظایف انجام‌شده", "عقب‌افتاده", "ساعات ثبت‌شده", "تعداد گزارش کار"];
        for (int i=0;i<headers.Length;i++) ws.Cell(1,i+1).Value=headers[i];
        for(int i=0;i<Stats.Count;i++){ var x=Stats[i]; int r=i+2; ws.Cell(r,1).Value=x.Name; ws.Cell(r,2).Value=x.Assigned; ws.Cell(r,3).Value=x.Completed; ws.Cell(r,4).Value=x.Overdue; ws.Cell(r,5).Value=x.Hours; ws.Cell(r,6).Value=x.Reports; }
        ws.RightToLeft=true; ws.Row(1).Style.Font.Bold=true; ws.Columns().AdjustToContents(); using var ms=new MemoryStream(); wb.SaveAs(ms);
        return File(ms.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "عملکرد-کارکنان.xlsx");
    }
}
