using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using TasksWebApp.Data;
using TasksWebApp.Models;

namespace TasksWebApp.Pages.Categories;

[Authorize(Roles = "Admin")]
public class IndexModel(AppDbContext db) : PageModel
{
    public List<Category> Categories { get; private set; } = [];

    [BindProperty]
    public Category Input { get; set; } = new();

    public async Task OnGetAsync() => await LoadAsync();

    public async Task<IActionResult> OnPostCreateAsync()
    {
        Input.Name = Input.Name?.Trim() ?? string.Empty;
        Input.Description = string.IsNullOrWhiteSpace(Input.Description) ? null : Input.Description.Trim();

        if (!ModelState.IsValid)
        {
            await LoadAsync();
            return Page();
        }

        db.Categories.Add(new Category { Name = Input.Name, Description = Input.Description });
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            ModelState.AddModelError("Input.Name", "دسته‌بندی با این نام از قبل وجود دارد.");
            await LoadAsync();
            return Page();
        }

        TempData["Message"] = "دسته‌بندی با موفقیت افزوده شد.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        var category = await db.Categories.Include(x => x.Tasks).FirstOrDefaultAsync(x => x.Id == id);
        if (category is null) return NotFound();

        if (category.Tasks.Count > 0)
        {
            TempData["Message"] = "دسته‌بندی دارای وظیفه است و قابل حذف نیست.";
            return RedirectToPage();
        }

        db.Categories.Remove(category);
        await db.SaveChangesAsync();
        TempData["Message"] = "دسته‌بندی حذف شد.";
        return RedirectToPage();
    }

    private async Task LoadAsync()
    {
        Categories = await db.Categories
            .AsNoTracking()
            .Include(x => x.Tasks)
            .OrderBy(x => x.Name)
            .ToListAsync();
    }
}
