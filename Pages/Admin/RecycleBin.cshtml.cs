using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using TasksWebApp.Data;
using TasksWebApp.Helpers;
using TasksWebApp.Models;

namespace TasksWebApp.Pages.Admin;

[Authorize(Roles = "Admin")]
public class RecycleBinModel(AppDbContext db, IWebHostEnvironment env) : PageModel
{
    public List<TaskItem> Items { get; private set; } = [];
    public async Task OnGetAsync()
    {
        Items = await db.TaskItems.IgnoreQueryFilters().Where(x => x.IsDeleted)
            .Include(x => x.CreatedBy).OrderByDescending(x => x.DeletedAt).Take(500).ToListAsync();
    }

    public async Task<IActionResult> OnPostRestoreAsync(int id)
    {
        var item = await db.TaskItems.FirstOrDefaultAsync(x => x.Id == id && x.IsDeleted);
        if (item is null) return NotFound();
        var all = await db.TaskItems.Where(x => x.IsDeleted).Select(x => new { x.Id, x.ParentTaskId }).ToListAsync();
        var ids = new HashSet<int> { id }; bool changed;
        do { changed = false; foreach (var child in all) if (child.ParentTaskId.HasValue && ids.Contains(child.ParentTaskId.Value) && ids.Add(child.Id)) changed = true; } while (changed);
        var restore = await db.TaskItems.Where(x => ids.Contains(x.Id)).ToListAsync();
        foreach (var task in restore) { task.IsDeleted = false; task.DeletedAt = null; task.DeletedById = null; }
        AuditWriter.Add(db, User, HttpContext, "بازیابی از سطل بازیافت", "TaskItem", id, $"تعداد بازیابی: {restore.Count}");
        await db.SaveChangesAsync(); TempData["Message"] = $"{restore.Count} مورد بازیابی شد."; return RedirectToPage();
    }

    public async Task<IActionResult> OnPostPermanentDeleteAsync(int id, bool confirmDelete)
    {
        if (!confirmDelete) { TempData["Message"] = "برای حذف دائمی باید تأیید را انتخاب کنی."; return RedirectToPage(); }
        var item = await db.TaskItems.FirstOrDefaultAsync(x => x.Id == id && x.IsDeleted);
        if (item is null) return NotFound();
        var all = await db.TaskItems.Select(x => new { x.Id, x.ParentTaskId }).ToListAsync();
        var ids = new HashSet<int> { id }; bool changed;
        do { changed = false; foreach (var child in all) if (child.ParentTaskId.HasValue && ids.Contains(child.ParentTaskId.Value) && ids.Add(child.Id)) changed = true; } while (changed);
        var tasks = await db.TaskItems.Where(x => ids.Contains(x.Id)).ToListAsync();
        if (tasks.Any(x => !x.IsDeleted)) { TempData["Message"] = "ابتدا همه زیر‌وظیفه‌های فعال را جداگانه به سطل بازیافت منتقل کن."; return RedirectToPage(); }
        var files = await db.TaskAttachments.Where(x => ids.Contains(x.TaskItemId)).ToListAsync();
        foreach (var f in files) { var path = Path.Combine(env.ContentRootPath, "App_Data", "TaskAttachments", f.StoredFileName); if (System.IO.File.Exists(path)) System.IO.File.Delete(path); }
        AuditWriter.Add(db, User, HttpContext, "حذف دائمی", "TaskItem", id, $"تعداد موارد: {tasks.Count}؛ عنوان: {item.Title}");
        db.TaskItems.RemoveRange(tasks);
        await db.SaveChangesAsync(); TempData["Message"] = "مورد و پیوست‌های آن برای همیشه حذف شدند."; return RedirectToPage();
    }
}
