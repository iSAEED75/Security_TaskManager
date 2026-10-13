using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using TasksWebApp.Data;
using TasksWebApp.Models;

namespace TasksWebApp.Pages.Admin;

[Authorize(Roles = "Admin")]
public class AuditLogModel(AppDbContext db) : PageModel
{
    public List<AuditLog> Items { get; private set; } = [];
    public async Task OnGetAsync(string? search = null)
    {
        Items = await db.AuditLogs.AsNoTracking()
            .Where(x => string.IsNullOrEmpty(search) || x.ActorName.Contains(search) || x.Action.Contains(search) || x.EntityType.Contains(search) || (x.Details != null && x.Details.Contains(search)))
            .OrderByDescending(x => x.CreatedAt).Take(1000).ToListAsync();
    }
}
