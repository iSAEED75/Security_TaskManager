using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using TasksWebApp.Data;
using TasksWebApp.Models;
using TasksWebApp.Helpers;

namespace TasksWebApp.Pages.Tasks;

[Authorize(Roles = "Admin")]
public class DeleteModel(AppDbContext db) : PageModel
{
    [BindProperty]
    public bool ConfirmDelete { get; set; }

    public TaskItem? Item { get; private set; }
    public int SubTaskCount { get; private set; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var loaded = await LoadItemAsync(id);
        if (loaded is null) return NotFound();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id)
    {
        var loaded = await LoadItemAsync(id);
        if (loaded is null) return NotFound();

        if (!ConfirmDelete)
        {
            ModelState.AddModelError(string.Empty, "برای حذف، تأیید حذف را انتخاب کنید.");
            return Page();
        }

        // حذف وظیفه اصلی همراه با زیر‌وظیفه‌های وابسته، تا رابطه خودارجاعی دیتابیس نقض نشود.
        var allTasks = await db.TaskItems
            .Where(x => x.ParentTaskId != null)
            .Select(x => new { x.Id, x.ParentTaskId })
            .ToListAsync();

        var idsToDelete = new HashSet<int> { id };
        bool changed;
        do
        {
            changed = false;
            foreach (var child in allTasks)
            {
                if (child.ParentTaskId.HasValue &&
                    idsToDelete.Contains(child.ParentTaskId.Value) &&
                    idsToDelete.Add(child.Id))
                    changed = true;
            }
        } while (changed);

        var deleteIds = idsToDelete.ToArray();
        var tasksToDelete = await db.TaskItems.Where(x => deleteIds.Contains(x.Id)).ToListAsync();
        var userId = int.TryParse(User.FindFirstValue(System.Security.Claims.ClaimTypes.NameIdentifier), out var actorId) ? actorId : (int?)null;
        foreach (var task in tasksToDelete)
        {
            task.IsDeleted = true;
            task.DeletedAt = DateTime.UtcNow;
            task.DeletedById = userId;
        }
        AuditWriter.Add(db, User, HttpContext, "انتقال به سطل بازیافت", "TaskItem", id,
            $"تعداد موارد منتقل‌شده: {tasksToDelete.Count}؛ عنوان: {Item?.Title}");
        await db.SaveChangesAsync();

        TempData["Message"] = tasksToDelete.Count > 1
            ? $"مورد اصلی و {tasksToDelete.Count - 1} زیر‌وظیفه به سطل بازیافت منتقل شدند."
            : "مورد به سطل بازیافت منتقل شد و قابل بازیابی است.";
        return RedirectToPage("Index");
    }

    private async Task<TaskItem?> LoadItemAsync(int id)
    {
        Item = await db.TaskItems
            .AsNoTracking()
            .Include(x => x.CreatedBy)
            .Include(x => x.AssignedTo)
            .FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);

        if (Item is null) return null;

        var descendants = await db.TaskItems
            .AsNoTracking()
            .Select(x => new { x.Id, x.ParentTaskId })
            .ToListAsync();

        var ids = new HashSet<int> { id };
        bool changed;
        do
        {
            changed = false;
            foreach (var child in descendants)
            {
                if (child.ParentTaskId.HasValue && ids.Contains(child.ParentTaskId.Value) && ids.Add(child.Id))
                    changed = true;
            }
        } while (changed);

        SubTaskCount = ids.Count - 1;
        return Item;
    }
}
