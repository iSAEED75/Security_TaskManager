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

public class EditModel(AppDbContext db) : PageModel
{
    [BindProperty] public TaskItem Item { get; set; } = new();
    [BindProperty] public string WorkDateShamsi { get; set; } = string.Empty;
    [BindProperty] public string? ReminderDateShamsi { get; set; }
    [BindProperty] public string? ReminderTime { get; set; }
    public SelectList Categories { get; private set; } = null!;
    public SelectList TaskLists { get; private set; } = null!;
    public SelectList Employees { get; private set; } = null!;
    public SelectList ParentTasks { get; private set; } = null!;

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var task = await db.TaskItems.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
        if (task is null) return NotFound();
        if (!CanManage(task)) return Forbid();
        Item = task;
        WorkDateShamsi = PersianDateHelper.Format(task.WorkDate);
        if (task.ReminderAt.HasValue)
        {
            ReminderDateShamsi = PersianDateHelper.Format(task.ReminderAt.Value);
            ReminderTime = task.ReminderAt.Value.ToString("HH:mm");
        }
        await LoadSelectListsAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var task = await db.TaskItems.FirstOrDefaultAsync(x => x.Id == Item.Id && !x.IsDeleted);
        if (task is null) return NotFound();
        if (!CanManage(task)) return Forbid();
        var userId = CurrentUserId();
        if (userId is null) return Challenge();

        // جلوگیری از تغییر مسئول وظیفه با ارسال مستقیم درخواست توسط کاربر عادی.
        if (!User.IsInRole("Admin"))
        {
            Item.AssignedToId = task.CreatedById == userId.Value
                ? userId.Value
                : task.AssignedToId;
        }

        if (!PersianDateHelper.TryParse(WorkDateShamsi, out var parsedDate)) ModelState.AddModelError(nameof(WorkDateShamsi), "تاریخ شمسی را به شکل ۱۴۰۵/۰۱/۰۱ وارد کنید.");
        else Item.WorkDate = parsedDate.Date;
        if (!string.IsNullOrWhiteSpace(ReminderDateShamsi))
        {
            if (!PersianDateHelper.TryParse(ReminderDateShamsi, out var reminderDate)) ModelState.AddModelError(nameof(ReminderDateShamsi), "تاریخ یادآوری را به‌صورت شمسی وارد کنید.");
            else if (!TimeSpan.TryParseExact(ReminderTime ?? "09:00", @"hh\:mm", CultureInfo.InvariantCulture, out var time)) ModelState.AddModelError(nameof(ReminderTime), "ساعت یادآوری معتبر نیست.");
            else Item.ReminderAt = reminderDate.Date.Add(time);
        }
        else Item.ReminderAt = null;

        if (!Enum.IsDefined(Item.RecordType)) ModelState.AddModelError("Item.RecordType", "نوع ثبت معتبر نیست.");
        if (!Enum.IsDefined(Item.Status)) ModelState.AddModelError("Item.Status", "وضعیت معتبر نیست.");
        if (!Enum.IsDefined(Item.Priority)) ModelState.AddModelError("Item.Priority", "اولویت معتبر نیست.");
        if (!Enum.IsDefined(Item.RecurrenceType)) ModelState.AddModelError("Item.RecurrenceType", "الگوی تکرار معتبر نیست.");
        if (string.IsNullOrWhiteSpace(Item.Description)) ModelState.AddModelError("Item.Description", "شرح فعالیت یا وظیفه الزامی است.");
        if (Item.CategoryId.HasValue && !await db.Categories.AnyAsync(x => x.Id == Item.CategoryId.Value)) ModelState.AddModelError("Item.CategoryId", "دسته‌بندی انتخاب‌شده وجود ندارد.");
        if (Item.TaskListId.HasValue && !await db.TaskLists.AnyAsync(x => x.Id == Item.TaskListId && x.OwnerUserId == task.CreatedById)) ModelState.AddModelError("Item.TaskListId", "فهرست انتخاب‌شده معتبر نیست.");
        if (Item.AssignedToId.HasValue && !await db.Users.AnyAsync(x => x.Id == Item.AssignedToId.Value)) ModelState.AddModelError("Item.AssignedToId", "کارمند انتخاب‌شده وجود ندارد.");
        if (Item.ParentTaskId == task.Id) ModelState.AddModelError("Item.ParentTaskId", "یک وظیفه نمی‌تواند زیرمجموعه خودش باشد.");
        if (Item.ParentTaskId.HasValue && !await db.TaskItems.AnyAsync(x => x.Id == Item.ParentTaskId && !x.IsDeleted && x.RecordType == TaskRecordType.Todo && x.CreatedById == task.CreatedById && x.Id != task.Id)) ModelState.AddModelError("Item.ParentTaskId", "وظیفه اصلی انتخاب‌شده معتبر نیست.");

        if (!ModelState.IsValid)
        {
            Item.CreatedById = task.CreatedById; Item.CreatedAt = task.CreatedAt;
            await LoadSelectListsAsync(); return Page();
        }

        task.Title = Item.Title.Trim(); task.RecordType = Item.RecordType;
        task.Description = string.IsNullOrWhiteSpace(Item.Description) ? null : Item.Description.Trim();
        task.Status = Item.Status; task.Priority = Item.Priority; task.WorkDate = Item.WorkDate.Date;
        task.DueDate = Item.RecordType == TaskRecordType.Todo ? Item.WorkDate.Date : null;
        task.DurationHours = Item.DurationHours; task.CategoryId = Item.CategoryId;
        task.AssignedToId = Item.AssignedToId; task.IsStarred = Item.IsStarred; task.SortOrder = Item.SortOrder;
        task.RecurrenceType = Item.RecordType == TaskRecordType.Todo ? Item.RecurrenceType : RecurrenceType.None;
        task.ReminderAt = Item.ReminderAt; task.ParentTaskId = Item.ParentTaskId; task.TaskListId = Item.TaskListId;
        AuditWriter.Add(db, User, HttpContext, "ویرایش", "TaskItem", task.Id, task.Title);
        await db.SaveChangesAsync();
        return RedirectToPage("Index");
    }

    private int? CurrentUserId() => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
    private bool CanManage(TaskItem task) => User.IsInRole("Admin") ||
        (CurrentUserId() is int id && (task.CreatedById == id || task.AssignedToId == id));

    private async Task LoadSelectListsAsync()
    {
        var ownerId = Item.CreatedById != 0 ? Item.CreatedById : CurrentUserId();
        Categories = new SelectList(await db.Categories.AsNoTracking().OrderBy(x => x.Name).ToListAsync(), nameof(Category.Id), nameof(Category.Name), Item.CategoryId);
        TaskLists = new SelectList(await db.TaskLists.AsNoTracking().Where(x => x.OwnerUserId == ownerId).OrderBy(x => x.Name).ToListAsync(), nameof(TaskList.Id), nameof(TaskList.Name), Item.TaskListId);
        var employees = User.IsInRole("Admin")
            ? await db.Users.AsNoTracking().OrderBy(x => x.FullName).ToListAsync()
            : await db.Users.AsNoTracking().Where(x => x.Id == CurrentUserId()).OrderBy(x => x.FullName).ToListAsync();
        Employees = new SelectList(employees, nameof(TasksWebApp.Models.User.Id), nameof(TasksWebApp.Models.User.FullName), Item.AssignedToId);
        ParentTasks = new SelectList(await db.TaskItems.AsNoTracking().Where(x => x.RecordType == TaskRecordType.Todo && !x.IsDeleted && x.CreatedById == ownerId && x.ParentTaskId == null && x.Id != Item.Id && x.Status != TaskStatus.Completed).OrderBy(x => x.Title).ToListAsync(), nameof(TaskItem.Id), nameof(TaskItem.Title), Item.ParentTaskId);
    }
}
