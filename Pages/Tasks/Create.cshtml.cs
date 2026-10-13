using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using TasksWebApp.Data;
using TasksWebApp.Helpers;
using TasksWebApp.Models;
using TaskStatus = TasksWebApp.Models.TaskStatus;

namespace TasksWebApp.Pages.Tasks;

public class CreateModel(AppDbContext db) : PageModel
{
    [BindProperty] public TaskItem Item { get; set; } = new();
    [BindProperty] public string WorkDateShamsi { get; set; } = PersianDateHelper.Format(DateTime.Today);
    [BindProperty] public string? ReminderDateShamsi { get; set; }
    [BindProperty] public string? ReminderTime { get; set; }
    [BindProperty(SupportsGet = true)] public int? ParentTaskId { get; set; }
    public SelectList Categories { get; private set; } = null!;
    public SelectList TaskLists { get; private set; } = null!;
    public SelectList Employees { get; private set; } = null!;
    public SelectList ParentTasks { get; private set; } = null!;

    public async Task OnGetAsync()
    {
        if (ParentTaskId.HasValue)
        {
            var userId = CurrentUserId();
            var parent = await db.TaskItems.AsNoTracking().FirstOrDefaultAsync(x => x.Id == ParentTaskId.Value && x.RecordType == TaskRecordType.Todo && !x.IsDeleted && x.ParentTaskId == null && (x.CreatedById == userId || User.IsInRole("Admin")));
            if (parent is not null)
            {
                Item.ParentTaskId = parent.Id;
                Item.TaskListId = parent.TaskListId;
            }
        }
        await LoadSelectListsAsync();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var userId = CurrentUserId();
        if (userId is null) return Challenge();

        // کاربران عادی فقط می‌توانند وظیفه را به خودشان تخصیص دهند؛ مقدار ارسالی مرورگر قابل اعتماد نیست.
        if (!User.IsInRole("Admin"))
            Item.AssignedToId = userId.Value;

        if (!PersianDateHelper.TryParse(WorkDateShamsi, out var parsedDate))
            ModelState.AddModelError(nameof(WorkDateShamsi), "تاریخ شمسی را به شکل ۱۴۰۵/۰۱/۰۱ وارد کنید.");
        else Item.WorkDate = parsedDate.Date;

        if (!string.IsNullOrWhiteSpace(ReminderDateShamsi))
        {
            if (!PersianDateHelper.TryParse(ReminderDateShamsi, out var reminderDate))
                ModelState.AddModelError(nameof(ReminderDateShamsi), "تاریخ یادآوری را به‌صورت شمسی وارد کنید.");
            else if (!TimeSpan.TryParseExact(ReminderTime ?? "09:00", @"hh\:mm", CultureInfo.InvariantCulture, out var time))
                ModelState.AddModelError(nameof(ReminderTime), "ساعت یادآوری معتبر نیست.");
            else Item.ReminderAt = reminderDate.Date.Add(time);
        }
        else Item.ReminderAt = null;

        if (!Enum.IsDefined(Item.RecordType)) ModelState.AddModelError("Item.RecordType", "نوع ثبت را انتخاب کنید.");
        if (!Enum.IsDefined(Item.Status)) ModelState.AddModelError("Item.Status", "وضعیت انتخاب‌شده معتبر نیست.");
        if (!Enum.IsDefined(Item.Priority)) ModelState.AddModelError("Item.Priority", "اولویت انتخاب‌شده معتبر نیست.");
        if (!Enum.IsDefined(Item.RecurrenceType)) ModelState.AddModelError("Item.RecurrenceType", "الگوی تکرار معتبر نیست.");
        if (string.IsNullOrWhiteSpace(Item.Description)) ModelState.AddModelError("Item.Description", "شرح فعالیت یا وظیفه الزامی است.");

        if (Item.CategoryId.HasValue && !await db.Categories.AnyAsync(x => x.Id == Item.CategoryId.Value))
            ModelState.AddModelError("Item.CategoryId", "دسته‌بندی انتخاب‌شده وجود ندارد.");
        if (Item.TaskListId.HasValue && !await db.TaskLists.AnyAsync(x => x.Id == Item.TaskListId && x.OwnerUserId == userId))
            ModelState.AddModelError("Item.TaskListId", "فهرست انتخاب‌شده معتبر نیست.");
        if (Item.AssignedToId.HasValue && !await db.Users.AnyAsync(x => x.Id == Item.AssignedToId.Value))
            ModelState.AddModelError("Item.AssignedToId", "کارمند انتخاب‌شده وجود ندارد.");
        if (Item.ParentTaskId.HasValue && !await db.TaskItems.AnyAsync(x => x.Id == Item.ParentTaskId && !x.IsDeleted && x.RecordType == TaskRecordType.Todo && (x.CreatedById == userId || User.IsInRole("Admin"))))
            ModelState.AddModelError("Item.ParentTaskId", "وظیفه اصلی انتخاب‌شده معتبر نیست.");

        if (!ModelState.IsValid) { await LoadSelectListsAsync(userId.Value); return Page(); }

        var task = new TaskItem
        {
            Title = Item.Title.Trim(), RecordType = Item.RecordType,
            Description = string.IsNullOrWhiteSpace(Item.Description) ? null : Item.Description.Trim(),
            Status = Item.Status, Priority = Item.Priority, WorkDate = Item.WorkDate.Date,
            DueDate = Item.RecordType == TaskRecordType.Todo ? Item.WorkDate.Date : null,
            DurationHours = Item.DurationHours, CategoryId = Item.CategoryId,
            AssignedToId = Item.AssignedToId, CreatedById = userId.Value, CreatedAt = DateTime.UtcNow,
            IsStarred = Item.IsStarred, SortOrder = Item.SortOrder, RecurrenceType = Item.RecordType == TaskRecordType.Todo ? Item.RecurrenceType : RecurrenceType.None,
            ReminderAt = Item.ReminderAt, ParentTaskId = Item.ParentTaskId, TaskListId = Item.TaskListId
        };
        if (!task.TaskListId.HasValue)
            task.TaskListId = await db.TaskLists.Where(x => x.OwnerUserId == userId.Value).Select(x => (int?)x.Id).FirstOrDefaultAsync();
        db.TaskItems.Add(task);
        await db.SaveChangesAsync();
        AuditWriter.Add(db, User, HttpContext, "ایجاد", "TaskItem", task.Id, $"{(task.RecordType == TaskRecordType.Todo ? "وظیفه" : "گزارش کار")}: {task.Title}");
        await db.SaveChangesAsync();
        return RedirectToPage("Index");
    }

    private int? CurrentUserId() => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    private async Task LoadSelectListsAsync(int? currentUserId = null)
    {
        currentUserId ??= CurrentUserId();
        Categories = new SelectList(await db.Categories.AsNoTracking().OrderBy(x => x.Name).ToListAsync(), nameof(Category.Id), nameof(Category.Name));
        TaskLists = new SelectList(await db.TaskLists.AsNoTracking().Where(x => x.OwnerUserId == currentUserId).OrderBy(x => x.Name).ToListAsync(), nameof(TaskList.Id), nameof(TaskList.Name), Item.TaskListId);
        var employees = User.IsInRole("Admin")
            ? await db.Users.AsNoTracking().OrderBy(x => x.FullName).ToListAsync()
            : await db.Users.AsNoTracking().Where(x => x.Id == currentUserId).OrderBy(x => x.FullName).ToListAsync();
        Employees = new SelectList(employees, nameof(TasksWebApp.Models.User.Id), nameof(TasksWebApp.Models.User.FullName), Item.AssignedToId);
        ParentTasks = new SelectList(await db.TaskItems.AsNoTracking().Where(x => x.RecordType == TaskRecordType.Todo && !x.IsDeleted && x.CreatedById == currentUserId && x.ParentTaskId == null && x.Status != TaskStatus.Completed).OrderBy(x => x.Title).ToListAsync(), nameof(TaskItem.Id), nameof(TaskItem.Title), Item.ParentTaskId);
    }
}
