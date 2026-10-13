using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using TasksWebApp.Data;
using TasksWebApp.Helpers;
using TasksWebApp.Models;

namespace TasksWebApp.Pages.Tasks;

public class FollowUpModel(AppDbContext db, IWebHostEnvironment env) : PageModel
{
    public TaskItem? Item { get; private set; }
    public List<TaskComment> Comments { get; private set; } = [];
    public List<TaskAttachment> Attachments { get; private set; } = [];
    [BindProperty] public string CommentText { get; set; } = string.Empty;
    [BindProperty] public IFormFile? Upload { get; set; }
    private int? CurrentUserId => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    public async Task<IActionResult> OnGetAsync(int id)
    {
        if (!await LoadAsync(id)) return NotFoundOrForbid(id);
        return Page();
    }

    public async Task<IActionResult> OnPostCommentAsync(int id)
    {
        if (!await LoadAsync(id)) return NotFoundOrForbid(id);
        if (CurrentUserId is null) return Challenge();
        var body = (CommentText ?? string.Empty).Trim();
        if (body.Length == 0 || body.Length > 2000)
        {
            ModelState.AddModelError(nameof(CommentText), "متن پیگیری باید بین ۱ تا ۲۰۰۰ نویسه باشد.");
            return Page();
        }
        db.TaskComments.Add(new TaskComment { TaskItemId = id, UserId = CurrentUserId.Value, Body = body, CreatedAt = DateTime.UtcNow });
        AuditWriter.Add(db, User, HttpContext, "ثبت پیگیری", "TaskItem", id, body.Length > 150 ? body[..150] : body);
        await db.SaveChangesAsync();
        TempData["Message"] = "یادداشت پیگیری ثبت شد.";
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostUploadAsync(int id)
    {
        if (!await LoadAsync(id)) return NotFoundOrForbid(id);
        if (CurrentUserId is null) return Challenge();
        if (Upload is null || Upload.Length == 0) { ModelState.AddModelError(nameof(Upload), "یک فایل انتخاب کن."); return Page(); }
        const long maxBytes = 10 * 1024 * 1024;
        if (Upload.Length > maxBytes) { ModelState.AddModelError(nameof(Upload), "حداکثر حجم فایل ۱۰ مگابایت است."); return Page(); }
        var originalName = Path.GetFileName(Upload.FileName);
        var ext = Path.GetExtension(originalName).ToLowerInvariant();
        string[] allowed = [".pdf", ".png", ".jpg", ".jpeg", ".webp", ".docx", ".xlsx", ".txt", ".csv"];
        if (!allowed.Contains(ext)) { ModelState.AddModelError(nameof(Upload), "این نوع فایل مجاز نیست."); return Page(); }
        if (ext is ".jpg" or ".jpeg" or ".png" or ".webp")
        {
            if (!await HasValidImageSignatureAsync(Upload, ext))
            {
                ModelState.AddModelError(nameof(Upload), "محتوای فایل با فرمت تصویر انتخاب‌شده مطابقت ندارد.");
                return Page();
            }
        }
        var stored = Guid.NewGuid().ToString("N") + ext;
        var folder = Path.Combine(env.ContentRootPath, "App_Data", "TaskAttachments");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, stored);
        await using (var stream = System.IO.File.Create(path)) await Upload.CopyToAsync(stream);
        db.TaskAttachments.Add(new TaskAttachment
        {
            TaskItemId = id, UploadedById = CurrentUserId.Value, OriginalFileName = originalName,
            StoredFileName = stored, ContentType = string.IsNullOrWhiteSpace(Upload.ContentType) ? "application/octet-stream" : Upload.ContentType,
            SizeBytes = Upload.Length, UploadedAt = DateTime.UtcNow
        });
        AuditWriter.Add(db, User, HttpContext, "بارگذاری پیوست", "TaskItem", id, originalName);
        await db.SaveChangesAsync();
        TempData["Message"] = "فایل پیوست شد.";
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnGetDownloadAsync(int attachmentId)
    {
        var attachment = await db.TaskAttachments.Include(x => x.TaskItem).FirstOrDefaultAsync(x => x.Id == attachmentId);
        if (attachment?.TaskItem is null || attachment.TaskItem.IsDeleted) return NotFound();
        if (!CanView(attachment.TaskItem)) return Forbid();
        var path = Path.Combine(env.ContentRootPath, "App_Data", "TaskAttachments", attachment.StoredFileName);
        if (!System.IO.File.Exists(path)) return NotFound("فایل روی سرور موجود نیست.");
        return PhysicalFile(path, attachment.ContentType, attachment.OriginalFileName);
    }

    public async Task<IActionResult> OnGetPreviewAsync(int attachmentId)
    {
        var attachment = await db.TaskAttachments.Include(x => x.TaskItem)
            .FirstOrDefaultAsync(x => x.Id == attachmentId);
        if (attachment?.TaskItem is null || attachment.TaskItem.IsDeleted) return NotFound();
        if (!CanView(attachment.TaskItem)) return Forbid();
        var ext = Path.GetExtension(attachment.StoredFileName).ToLowerInvariant();
        var contentType = ext switch { ".jpg" or ".jpeg" => "image/jpeg", ".png" => "image/png", ".webp" => "image/webp", _ => null };
        if (contentType is null) return BadRequest("این پیوست تصویر قابل نمایش نیست.");
        var path = Path.Combine(env.ContentRootPath, "App_Data", "TaskAttachments", attachment.StoredFileName);
        if (!System.IO.File.Exists(path)) return NotFound();
        Response.Headers.CacheControl = "private, no-store";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return new PhysicalFileResult(path, contentType) { EnableRangeProcessing = false };
    }

    private static async Task<bool> HasValidImageSignatureAsync(IFormFile file, string extension)
    {
        var header = new byte[12];
        await using var stream = file.OpenReadStream();
        var read = await stream.ReadAsync(header.AsMemory(0, header.Length));
        if (extension is ".jpg" or ".jpeg") return read >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF;
        if (extension == ".png") return read >= 8 && header.Take(8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        if (extension == ".webp") return read >= 12 && header.Take(4).SequenceEqual("RIFF"u8.ToArray()) && header.Skip(8).Take(4).SequenceEqual("WEBP"u8.ToArray());
        return false;
    }

    private async Task<bool> LoadAsync(int id)
    {
        Item = await db.TaskItems.AsNoTracking().Include(x => x.CreatedBy).Include(x => x.AssignedTo).Include(x => x.TaskList).Include(x => x.ParentTask).Include(x => x.SubTasks)
            .FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
        if (Item is null || !CanView(Item)) return false;
        Comments = await db.TaskComments.AsNoTracking().Include(x => x.User).Where(x => x.TaskItemId == id).OrderByDescending(x => x.CreatedAt).ToListAsync();
        Attachments = await db.TaskAttachments.AsNoTracking().Include(x => x.UploadedBy).Where(x => x.TaskItemId == id).OrderByDescending(x => x.UploadedAt).ToListAsync();
        return true;
    }
    private bool CanView(TaskItem task) => User.IsInRole("Admin") || (CurrentUserId is int id && (task.CreatedById == id || task.AssignedToId == id));
    private IActionResult NotFoundOrForbid(int id) => db.TaskItems.Any(x => x.Id == id) ? Forbid() : NotFound();
}
