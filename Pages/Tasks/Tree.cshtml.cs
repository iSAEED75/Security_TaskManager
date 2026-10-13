using System.Security.Claims;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using TasksWebApp.Data;
using TasksWebApp.Models;

namespace TasksWebApp.Pages.Tasks;

public class TreeModel(AppDbContext db) : PageModel
{
    public List<TaskTreeNode> Roots { get; private set; } = [];
    public bool IsAdmin => User.IsInRole("Admin");

    public async Task OnGetAsync()
    {
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId);
        var query = db.TaskItems.AsNoTracking().Where(x => !x.IsDeleted);
        if (!IsAdmin) query = query.Where(x => x.CreatedById == userId || x.AssignedToId == userId);
        var items = await query.Include(x => x.TaskList).OrderBy(x => x.SortOrder).ThenBy(x => x.WorkDate).ThenBy(x => x.Title).ToListAsync();
        var nodes = items.ToDictionary(x => x.Id, x => new TaskTreeNode { Item = x });
        foreach (var node in nodes.Values)
        {
            if (node.Item.ParentTaskId is int parentId && nodes.TryGetValue(parentId, out var parent))
                parent.Children.Add(node);
        }
        Roots = nodes.Values.Where(x => x.Item.ParentTaskId is null || !nodes.ContainsKey(x.Item.ParentTaskId.Value)).ToList();
    }
}
