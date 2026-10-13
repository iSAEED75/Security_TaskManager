using System.ComponentModel.DataAnnotations;

namespace TasksWebApp.Models;

public class TaskAttachment
{
    public int Id { get; set; }
    public int TaskItemId { get; set; }
    public int UploadedById { get; set; }
    [Required, StringLength(255)] public string OriginalFileName { get; set; } = string.Empty;
    [Required, StringLength(100)] public string StoredFileName { get; set; } = string.Empty;
    [StringLength(150)] public string ContentType { get; set; } = "application/octet-stream";
    public long SizeBytes { get; set; }
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
    public User? UploadedBy { get; set; }
    public TaskItem? TaskItem { get; set; }
}
