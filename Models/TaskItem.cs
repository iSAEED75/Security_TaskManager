using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace TasksWebApp.Models;

public class TaskItem
{
    public int Id { get; set; }

    [EnumDataType(typeof(TaskRecordType), ErrorMessage = "نوع ثبت انتخاب‌شده معتبر نیست.")]
    public TaskRecordType RecordType { get; set; } = TaskRecordType.WorkReport;

    [Required(ErrorMessage = "عنوان گزارش الزامی است.")]
    [StringLength(200, ErrorMessage = "عنوان نمی‌تواند بیشتر از ۲۰۰ نویسه باشد.")]
    public string Title { get; set; } = string.Empty;

    [StringLength(2000, ErrorMessage = "شرح نمی‌تواند بیشتر از ۲۰۰۰ نویسه باشد.")]
    public string? Description { get; set; }

    [EnumDataType(typeof(TaskStatus), ErrorMessage = "وضعیت انتخاب‌شده معتبر نیست.")]
    public TaskStatus Status { get; set; } = TaskStatus.Pending;

    [EnumDataType(typeof(TaskPriority), ErrorMessage = "اولویت انتخاب‌شده معتبر نیست.")]
    public TaskPriority Priority { get; set; } = TaskPriority.Medium;

    public DateTime WorkDate { get; set; } = DateTime.Today;

    [Range(typeof(decimal), "0.25", "24", ErrorMessage = "مدت کار باید بین ۰٫۲۵ تا ۲۴ ساعت باشد.")]
    public decimal DurationHours { get; set; } = 1m;

    // Kept for compatibility with records created by the earlier task-management version.
    public DateTime? DueDate { get; set; }

    public bool IsStarred { get; set; }
    public int SortOrder { get; set; }
    public RecurrenceType RecurrenceType { get; set; } = RecurrenceType.None;
    public DateTime? ReminderAt { get; set; }

    public int? ParentTaskId { get; set; }
    [ValidateNever]
    public TaskItem? ParentTask { get; set; }
    [ValidateNever]
    public ICollection<TaskItem> SubTasks { get; set; } = new List<TaskItem>();

    public int? TaskListId { get; set; }
    [ValidateNever]
    public TaskList? TaskList { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public int? DeletedById { get; set; }

    public int CreatedById { get; set; }
    [ValidateNever]
    public User CreatedBy { get; set; } = null!;

    public int? AssignedToId { get; set; }
    [ValidateNever]
    public User? AssignedTo { get; set; }

    public int? CategoryId { get; set; }
    [ValidateNever]
    public Category? Category { get; set; }
}
