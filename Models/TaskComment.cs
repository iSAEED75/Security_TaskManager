using System.ComponentModel.DataAnnotations;

namespace TasksWebApp.Models;

public class TaskComment
{
    public int Id { get; set; }
    public int TaskItemId { get; set; }
    public int UserId { get; set; }
    [Required, StringLength(2000)] public string Body { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public User? User { get; set; }
    public TaskItem? TaskItem { get; set; }
}
