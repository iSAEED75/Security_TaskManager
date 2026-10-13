using System.ComponentModel.DataAnnotations;

namespace TasksWebApp.Models;

public class AuditLog
{
    public long Id { get; set; }
    public int? ActorUserId { get; set; }
    [StringLength(150)] public string ActorName { get; set; } = "سامانه";
    [Required, StringLength(80)] public string Action { get; set; } = string.Empty;
    [Required, StringLength(80)] public string EntityType { get; set; } = string.Empty;
    public int? EntityId { get; set; }
    [StringLength(2000)] public string? Details { get; set; }
    [StringLength(64)] public string? IpAddress { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
