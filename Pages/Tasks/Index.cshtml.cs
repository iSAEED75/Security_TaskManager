using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using TasksWebApp.Data;
using TasksWebApp.Helpers;
using TasksWebApp.Models;
using TaskStatus = TasksWebApp.Models.TaskStatus;

namespace TasksWebApp.Pages.Tasks;

public class IndexModel(AppDbContext db) : PageModel
{
    private const int PageSize = 50;
    public List<TaskItem> Items { get; private set; } = [];
    public List<Category> Categories { get; private set; } = [];
    public List<TaskList> Lists { get; private set; } = [];
    public List<User> Employees { get; private set; } = [];
    public List<TaskItem> ReminderItems { get; private set; } = [];
    public List<TaskItem> ScheduledReminders { get; private set; } = [];
    public bool IsAdmin => User.IsInRole("Admin");
    public int PageNumber { get; private set; } = 1;
    public int TotalPages { get; private set; } = 1;
    public int OpenCount { get; private set; }
    public int OverdueCount { get; private set; }
    public int CompletedCount { get; private set; }
    public int TodayCount { get; private set; }

    [BindProperty(SupportsGet = true)] public int? Status { get; set; }
    [BindProperty(SupportsGet = true)] public int? Priority { get; set; }
    [BindProperty(SupportsGet = true)] public int? CategoryId { get; set; }
    [BindProperty(SupportsGet = true)] public int? TaskListId { get; set; }
    [BindProperty(SupportsGet = true)] public int? AssignedToId { get; set; }
    [BindProperty(SupportsGet = true)] public int? RecordType { get; set; }
    [BindProperty(SupportsGet = true)] public bool? Starred { get; set; }
    [BindProperty(SupportsGet = true)] public bool? OverdueOnly { get; set; }
    [BindProperty(SupportsGet = true)] public string? FromShamsi { get; set; }
    [BindProperty(SupportsGet = true)] public string? ToShamsi { get; set; }
    [BindProperty(SupportsGet = true)] public string? Search { get; set; }
    [BindProperty(SupportsGet = true)] public string Sort { get; set; } = "due";
    [BindProperty(SupportsGet = true, Name = "page")] public int P { get; set; } = 1;

    public async Task<IActionResult> OnGetAsync()
    {
        var userId = CurrentUserId();
        if (userId is null) return Challenge();
        Categories = await db.Categories.AsNoTracking().OrderBy(x => x.Name).ToListAsync();
        Lists = await db.TaskLists.AsNoTracking().Where(x => IsAdmin || x.OwnerUserId == userId).OrderBy(x => x.Name).ToListAsync();
        Employees = IsAdmin ? await db.Users.AsNoTracking().OrderBy(x => x.FullName).ToListAsync() : [];

        var baseQuery = VisibleQuery(userId.Value);
        var today = DateTime.Today;
        OpenCount = await baseQuery.CountAsync(x => x.RecordType == TaskRecordType.Todo && x.Status != TaskStatus.Completed);
        CompletedCount = await baseQuery.CountAsync(x => x.Status == TaskStatus.Completed);
        OverdueCount = await baseQuery.CountAsync(x => x.RecordType == TaskRecordType.Todo && x.Status != TaskStatus.Completed && x.WorkDate < today);
        TodayCount = await baseQuery.CountAsync(x => x.RecordType == TaskRecordType.Todo && x.Status != TaskStatus.Completed && x.WorkDate == today);
        ReminderItems = await baseQuery.Where(x => x.RecordType == TaskRecordType.Todo && x.Status != TaskStatus.Completed && x.ReminderAt != null && x.ReminderAt <= DateTime.Now)
            .OrderBy(x => x.ReminderAt).Take(8).ToListAsync();
        ScheduledReminders = await baseQuery.Where(x => x.RecordType == TaskRecordType.Todo && x.Status != TaskStatus.Completed && x.ReminderAt != null && x.ReminderAt > DateTime.Now && x.ReminderAt <= DateTime.Now.AddDays(7))
            .OrderBy(x => x.ReminderAt).Take(100).ToListAsync();

        var query = baseQuery;
        if (Status.HasValue && Enum.IsDefined(typeof(TaskStatus), Status.Value)) query = query.Where(x => (int)x.Status == Status.Value);
        if (Priority.HasValue && Enum.IsDefined(typeof(TaskPriority), Priority.Value)) query = query.Where(x => (int)x.Priority == Priority.Value);
        if (CategoryId.HasValue) query = query.Where(x => x.CategoryId == CategoryId.Value);
        if (TaskListId.HasValue) query = query.Where(x => x.TaskListId == TaskListId.Value);
        if (AssignedToId.HasValue) query = query.Where(x => x.AssignedToId == AssignedToId.Value);
        if (RecordType.HasValue && Enum.IsDefined(typeof(TaskRecordType), RecordType.Value)) query = query.Where(x => (int)x.RecordType == RecordType.Value);
        if (Starred == true) query = query.Where(x => x.IsStarred);
        if (OverdueOnly == true) query = query.Where(x => x.RecordType == TaskRecordType.Todo && x.Status != TaskStatus.Completed && x.WorkDate < today);
        if (PersianDateHelper.TryParse(FromShamsi, out var from)) query = query.Where(x => x.WorkDate >= from.Date);
        if (PersianDateHelper.TryParse(ToShamsi, out var to)) query = query.Where(x => x.WorkDate < to.Date.AddDays(1));
        if (!string.IsNullOrWhiteSpace(Search))
        {
            Search = Search.Trim();
            if (Search.Length > 200) Search = Search[..200];
            query = query.Where(x => x.Title.Contains(Search) || (x.Description != null && x.Description.Contains(Search)));
        }

        var count = await query.CountAsync();
        TotalPages = Math.Max(1, (int)Math.Ceiling(count / (double)PageSize));
        PageNumber = Math.Clamp(P, 1, TotalPages);
        query = Sort switch
        {
            "priority" => query.OrderByDescending(x => x.Priority).ThenBy(x => x.WorkDate),
            "created" => query.OrderByDescending(x => x.CreatedAt),
            "custom" => query.OrderBy(x => x.SortOrder).ThenBy(x => x.WorkDate),
            "starred" => query.OrderByDescending(x => x.IsStarred).ThenBy(x => x.WorkDate),
            "title" => query.OrderBy(x => x.Title),
            _ => query.OrderBy(x => x.Status == TaskStatus.Completed).ThenBy(x => x.WorkDate).ThenByDescending(x => x.Priority)
        };
        Items = await query.Include(x => x.CreatedBy).Include(x => x.AssignedTo).Include(x => x.Category)
            .Include(x => x.TaskList).Include(x => x.SubTasks)
            .Skip((PageNumber - 1) * PageSize).Take(PageSize).ToListAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostToggleStatusAsync(int id)
    {
        var task = await db.TaskItems.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
        if (task is null) return NotFound();
        if (!CanManage(task)) return Forbid();
        if (task.Status == TaskStatus.Completed)
            task.Status = TaskStatus.Pending;
        else
        {
            task.Status = TaskStatus.Completed;
            CreateNextRecurringTask(task);
        }
        AuditWriter.Add(db, User, HttpContext, "تغییر وضعیت", "TaskItem", task.Id, $"وضعیت جدید: {task.Status}");
        await db.SaveChangesAsync();
        return RedirectToPage(new { Search, Status, Priority, CategoryId, TaskListId, AssignedToId, RecordType, Starred, OverdueOnly, FromShamsi, ToShamsi, Sort });
    }

    public async Task<IActionResult> OnPostToggleStarAsync(int id)
    {
        var task = await db.TaskItems.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
        if (task is null) return NotFound();
        if (!CanManage(task)) return Forbid();
        task.IsStarred = !task.IsStarred;
        AuditWriter.Add(db, User, HttpContext, "تغییر اهمیت", "TaskItem", task.Id, task.IsStarred ? "ستاره‌دار شد" : "ستاره برداشته شد");
        await db.SaveChangesAsync();
        return RedirectToPage(new { Search, Status, Priority, CategoryId, TaskListId, AssignedToId, RecordType, Starred, OverdueOnly, FromShamsi, ToShamsi, Sort });
    }

    public async Task<IActionResult> OnPostConvertToReportAsync(int id)
    {
        var task = await db.TaskItems.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
        if (task is null) return NotFound();
        if (!CanManage(task)) return Forbid();
        if (task.RecordType != TaskRecordType.Todo || task.Status != TaskStatus.Completed)
        {
            TempDataMessage("فقط وظیفه‌های انجام‌شده را می‌توان به گزارش کار تبدیل کرد.");
            return RedirectToPage();
        }
        task.RecordType = TaskRecordType.WorkReport;
        task.WorkDate = DateTime.Today;
        task.DueDate = null;
        task.RecurrenceType = RecurrenceType.None;
        task.ReminderAt = null;
        AuditWriter.Add(db, User, HttpContext, "تبدیل به گزارش کار", "TaskItem", task.Id, task.Title);
        await db.SaveChangesAsync();
        return RedirectToPage();
    }

    private void TempDataMessage(string message) => TempData["Message"] = message;
    private IQueryable<TaskItem> VisibleQuery(int userId)
    {
        var q = db.TaskItems.AsNoTracking().Where(x => !x.IsDeleted).AsQueryable();
        if (!IsAdmin) q = q.Where(x => x.CreatedById == userId || x.AssignedToId == userId);
        return q;
    }
    private int? CurrentUserId() => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
    private bool CanManage(TaskItem task) => IsAdmin || (CurrentUserId() is int id && (task.CreatedById == id || task.AssignedToId == id));

    private void CreateNextRecurringTask(TaskItem task)
    {
        if (task.RecordType != TaskRecordType.Todo || task.RecurrenceType == RecurrenceType.None) return;
        var nextDate = task.RecurrenceType switch
        {
            RecurrenceType.Daily => task.WorkDate.AddDays(1),
            RecurrenceType.Weekly => task.WorkDate.AddDays(7),
            RecurrenceType.Monthly => task.WorkDate.AddMonths(1),
            RecurrenceType.Yearly => task.WorkDate.AddYears(1),
            _ => task.WorkDate
        };
        if (db.TaskItems.Any(x => x.CreatedById == task.CreatedById && x.Title == task.Title && x.WorkDate == nextDate && x.RecurrenceType == task.RecurrenceType && x.ParentTaskId == task.ParentTaskId)) return;
        db.TaskItems.Add(new TaskItem
        {
            Title = task.Title, Description = task.Description, RecordType = TaskRecordType.Todo,
            Status = TaskStatus.Pending, Priority = task.Priority, WorkDate = nextDate, DueDate = nextDate,
            DurationHours = task.DurationHours, CreatedById = task.CreatedById, CreatedAt = DateTime.UtcNow,
            AssignedToId = task.AssignedToId, CategoryId = task.CategoryId, TaskListId = task.TaskListId,
            ParentTaskId = task.ParentTaskId, IsStarred = task.IsStarred, SortOrder = task.SortOrder,
            RecurrenceType = task.RecurrenceType,
            ReminderAt = task.ReminderAt.HasValue ? task.ReminderAt.Value.Add(nextDate.Date - task.WorkDate.Date) : null
        });
    }
}
