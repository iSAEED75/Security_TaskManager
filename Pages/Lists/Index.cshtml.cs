using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using TasksWebApp.Data;
using TasksWebApp.Models;

namespace TasksWebApp.Pages.Lists;

public class IndexModel(AppDbContext db) : PageModel
{
    [BindProperty] public string Name { get; set; } = string.Empty;
    public List<(TaskList List, int Count)> Items { get; private set; } = [];
    private int? UserId => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    public async Task<IActionResult> OnGetAsync()
    {
        if (UserId is null) return Challenge();
        var lists = await db.TaskLists.AsNoTracking().Where(x => x.OwnerUserId == UserId).OrderBy(x => x.Name).ToListAsync();
        var counts = await db.TaskItems.AsNoTracking().Where(x => !x.IsDeleted && x.TaskListId != null).GroupBy(x => x.TaskListId).Select(g => new { Id = g.Key!.Value, Count = g.Count() }).ToDictionaryAsync(x => x.Id, x => x.Count);
        Items = lists.Select(x => (x, counts.GetValueOrDefault(x.Id))).ToList();
        return Page();
    }

    public async Task<IActionResult> OnPostCreateAsync()
    {
        if (UserId is null) return Challenge();
        Name = (Name ?? string.Empty).Trim();
        if (Name.Length == 0 || Name.Length > 100)
        {
            ModelState.AddModelError(nameof(Name), "نام فهرست را وارد کنید (حداکثر ۱۰۰ نویسه).");
            await LoadItemsAsync();
            return Page();
        }
        if (await db.TaskLists.AnyAsync(x => x.OwnerUserId == UserId && x.Name == Name))
        {
            ModelState.AddModelError(nameof(Name), "فهرستی با این نام وجود دارد.");
            await LoadItemsAsync();
            return Page();
        }
        db.TaskLists.Add(new TaskList { Name = Name, OwnerUserId = UserId.Value, CreatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        if (UserId is null) return Challenge();
        var list = await db.TaskLists.FirstOrDefaultAsync(x => x.Id == id && x.OwnerUserId == UserId);
        if (list is null) return NotFound();
        // وظایف حذف نمی‌شوند؛ فقط از فهرست جدا خواهند شد.
        var tasks = await db.TaskItems.Where(x => !x.IsDeleted && x.TaskListId == id).ToListAsync();
        foreach (var task in tasks) task.TaskListId = null;
        db.TaskLists.Remove(list);
        await db.SaveChangesAsync();
        return RedirectToPage();
    }

    private async Task LoadItemsAsync()
    {
        if (UserId is null) return;
        var lists = await db.TaskLists.AsNoTracking().Where(x => x.OwnerUserId == UserId).OrderBy(x => x.Name).ToListAsync();
        var counts = await db.TaskItems.AsNoTracking().Where(x => !x.IsDeleted && x.TaskListId != null).GroupBy(x => x.TaskListId).Select(g => new { Id = g.Key!.Value, Count = g.Count() }).ToDictionaryAsync(x => x.Id, x => x.Count);
        Items = lists.Select(x => (x, counts.GetValueOrDefault(x.Id))).ToList();
    }
}
