using System.ComponentModel.DataAnnotations;

namespace TasksWebApp.Models;

public class User
{
    public int Id { get; set; }

    [Required, StringLength(100)]
    public string Username { get; set; } = string.Empty;

    [Required]
    public string PasswordHash { get; set; } = string.Empty;

    [Required, StringLength(150)]
    public string FullName { get; set; } = string.Empty;

    [Required, StringLength(50)]
    public string EmployeeNumber { get; set; } = string.Empty;

    [Required, StringLength(30)]
    public string Role { get; set; } = "User";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [StringLength(100)]
    public string? ProfilePhotoFileName { get; set; }

    public DateTime? LastLoginAt { get; set; }
    public DateTime? LastFailedLoginAt { get; set; }
    public int FailedLoginCount { get; set; }
    public bool MustChangePassword { get; set; }
    public DateTime? LastPasswordChangedAt { get; set; }

    public ICollection<TaskItem> CreatedTasks { get; set; } = new List<TaskItem>();
    public ICollection<TaskItem> AssignedTasks { get; set; } = new List<TaskItem>();
}
