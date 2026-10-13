using System.ComponentModel.DataAnnotations;

namespace TasksWebApp.Models;

public class Category
{
    public int Id { get; set; }

    [Required(ErrorMessage = "نام دسته‌بندی الزامی است.")]
    [StringLength(100, ErrorMessage = "نام دسته‌بندی نمی‌تواند بیشتر از ۱۰۰ نویسه باشد.")]
    public string Name { get; set; } = string.Empty;

    [StringLength(500, ErrorMessage = "توضیحات نمی‌تواند بیشتر از ۵۰۰ نویسه باشد.")]
    public string? Description { get; set; }

    public ICollection<TaskItem> Tasks { get; set; } = new List<TaskItem>();
}
