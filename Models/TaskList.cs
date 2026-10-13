using System.ComponentModel.DataAnnotations;

namespace TasksWebApp.Models;

public class TaskList
{
    public int Id { get; set; }
    [Required, StringLength(100)]
    public string Name { get; set; } = string.Empty;
    public int OwnerUserId { get; set; }
    public User OwnerUser { get; set; } = null!;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public ICollection<TaskItem> Tasks { get; set; } = new List<TaskItem>();
}
