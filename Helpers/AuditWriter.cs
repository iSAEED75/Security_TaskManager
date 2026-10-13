using System.Security.Claims;
using TasksWebApp.Data;
using TasksWebApp.Models;

namespace TasksWebApp.Helpers;

public static class AuditWriter
{
    public static void Add(AppDbContext db, ClaimsPrincipal user, HttpContext context, string action, string entityType, int? entityId = null, string? details = null)
    {
        int? userId = int.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
        db.AuditLogs.Add(new AuditLog
        {
            ActorUserId = userId,
            ActorName = user.Identity?.Name ?? "کاربر ناشناس",
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            Details = details,
            IpAddress = context.Connection.RemoteIpAddress?.ToString(),
            CreatedAt = DateTime.UtcNow
        });
    }
}
