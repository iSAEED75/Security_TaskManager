using System.IO.Compression;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using TasksWebApp.Data;
using TasksWebApp.Helpers;
using TasksWebApp.Models;
using TaskStatus = TasksWebApp.Models.TaskStatus;

namespace TasksWebApp.Pages.Admin;

[Authorize(Roles = "Admin")]
public class BackupModel(AppDbContext db, IWebHostEnvironment env) : PageModel
{
    [BindProperty] public IFormFile? BackupFile { get; set; }
    [BindProperty] public bool ConfirmRestore { get; set; }
    public string? ResultMessage { get; private set; }
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true, WriteIndented = true };

    public IActionResult OnGet() => Page();

    public async Task<IActionResult> OnGetDownloadAsync()
    {
        var users = await db.Users.AsNoTracking().ToListAsync();
        var byId = users.ToDictionary(x => x.Id, x => x.Username);
        var tasks = await db.TaskItems.AsNoTracking().ToListAsync();
        var categories = await db.Categories.AsNoTracking().ToListAsync();
        var lists = await db.TaskLists.AsNoTracking().ToListAsync();
        var comments = await db.TaskComments.AsNoTracking().ToListAsync();
        var attachments = await db.TaskAttachments.AsNoTracking().ToListAsync();
        var audit = await db.AuditLogs.AsNoTracking().OrderBy(x => x.Id).ToListAsync();
        var categoryById = categories.ToDictionary(x => x.Id, x => x.Name);
        var listById = lists.ToDictionary(x => x.Id, x => x);
        var payload = new BackupPayload
        {
            CreatedAt = DateTime.UtcNow,
            Categories = categories.Select(x => new CategoryDto(x.Name, x.Description)).ToList(),
            Lists = lists.Select(x => new ListDto(x.Name, byId.GetValueOrDefault(x.OwnerUserId, ""), x.CreatedAt)).ToList(),
            Tasks = tasks.Select(x => new TaskDto(x.Id, x.RecordType, x.Title, x.Description, x.Status, x.Priority, x.WorkDate, x.DurationHours, x.DueDate, x.IsStarred, x.SortOrder, x.RecurrenceType, x.ReminderAt, x.ParentTaskId, x.TaskListId, x.CategoryId, byId.GetValueOrDefault(x.CreatedById, ""), x.AssignedToId.HasValue ? byId.GetValueOrDefault(x.AssignedToId.Value, null) : null, x.CreatedAt, x.IsDeleted, x.DeletedAt, x.DeletedById.HasValue ? byId.GetValueOrDefault(x.DeletedById.Value, null) : null)
            { CategoryName = x.CategoryId.HasValue ? categoryById.GetValueOrDefault(x.CategoryId.Value) : null,
              ListName = x.TaskListId.HasValue && listById.ContainsKey(x.TaskListId.Value) ? listById[x.TaskListId.Value].Name : null,
              ListOwnerUsername = x.TaskListId.HasValue && listById.ContainsKey(x.TaskListId.Value) ? byId.GetValueOrDefault(listById[x.TaskListId.Value].OwnerUserId, "") : null }).ToList(),
            Comments = comments.Select(x => new CommentDto(x.TaskItemId, byId.GetValueOrDefault(x.UserId, ""), x.Body, x.CreatedAt)).ToList(),
            Attachments = attachments.Select(x => new AttachmentDto(x.TaskItemId, byId.GetValueOrDefault(x.UploadedById, ""), x.OriginalFileName, x.StoredFileName, x.ContentType, x.SizeBytes, x.UploadedAt)).ToList(),
            AuditLogs = audit.Select(x => new AuditDto(x.ActorUserId.HasValue ? byId.GetValueOrDefault(x.ActorUserId.Value, null) : null, x.ActorName, x.Action, x.EntityType, x.EntityId, x.Details, x.IpAddress, x.CreatedAt)).ToList()
        };
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true))
        {
            var entry = zip.CreateEntry("backup.json", CompressionLevel.Optimal);
            await using (var stream = entry.Open()) await JsonSerializer.SerializeAsync(stream, payload, JsonOptions);
            var folder = Path.Combine(env.ContentRootPath, "App_Data", "TaskAttachments");
            foreach (var file in attachments)
            {
                var path = Path.Combine(folder, file.StoredFileName);
                if (!System.IO.File.Exists(path)) continue;
                var fileEntry = zip.CreateEntry("attachments/" + file.StoredFileName, CompressionLevel.Optimal);
                await using var target = fileEntry.Open(); await using var source = System.IO.File.OpenRead(path); await source.CopyToAsync(target);
            }
        }
        AuditWriter.Add(db, User, HttpContext, "تهیه نسخه پشتیبان", "Backup", null, $"تعداد وظایف: {tasks.Count}، پیوست‌ها: {attachments.Count}");
        await db.SaveChangesAsync();
        return File(output.ToArray(), "application/zip", $"TasksWebApp-Backup-{DateTime.Now:yyyyMMdd-HHmm}.zip");
    }

    public async Task<IActionResult> OnPostRestoreAsync()
    {
        if (!ConfirmRestore) { ModelState.AddModelError(string.Empty, "تأیید بازیابی را انتخاب کن."); return Page(); }
        if (BackupFile is null || BackupFile.Length == 0) { ModelState.AddModelError(nameof(BackupFile), "فایل نسخه پشتیبان را انتخاب کن."); return Page(); }
        if (BackupFile.Length > 100 * 1024 * 1024) { ModelState.AddModelError(nameof(BackupFile), "حداکثر حجم فایل پشتیبان ۱۰۰ مگابایت است."); return Page(); }
        BackupPayload? payload;
        using var zipStream = BackupFile.OpenReadStream();
        using var zip = new ZipArchive(zipStream, ZipArchiveMode.Read, false);
        var jsonEntry = zip.GetEntry("backup.json");
        if (jsonEntry is null) { ModelState.AddModelError(string.Empty, "فایل پشتیبان معتبر نیست یا backup.json ندارد."); return Page(); }
        await using (var stream = jsonEntry.Open()) payload = await JsonSerializer.DeserializeAsync<BackupPayload>(stream, JsonOptions);
        if (payload is null || payload.Tasks is null || payload.Tasks.Count > 100000) { ModelState.AddModelError(string.Empty, "ساختار فایل پشتیبان معتبر نیست."); return Page(); }

        var users = await db.Users.ToListAsync();
        var userByName = users.ToDictionary(x => x.Username, StringComparer.OrdinalIgnoreCase);
        var currentId = int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var uid) ? uid : users.FirstOrDefault()?.Id ?? 0;
        int MapUser(string? name) => !string.IsNullOrWhiteSpace(name) && userByName.TryGetValue(name, out var u) ? u.Id : currentId;

        foreach (var c in payload.Categories ?? [])
            if (!await db.Categories.AnyAsync(x => x.Name == c.Name)) db.Categories.Add(new Category { Name = c.Name, Description = c.Description });
        await db.SaveChangesAsync();
        var categoryByName = await db.Categories.ToDictionaryAsync(x => x.Name, x => x.Id);

        foreach (var l in payload.Lists ?? [])
        {
            var owner = MapUser(l.OwnerUsername);
            if (owner != 0 && !await db.TaskLists.AnyAsync(x => x.OwnerUserId == owner && x.Name == l.Name))
                db.TaskLists.Add(new TaskList { Name = l.Name, OwnerUserId = owner, CreatedAt = l.CreatedAt });
        }
        await db.SaveChangesAsync();
        var listLookup = await db.TaskLists.ToListAsync();
        var listMap = (payload.Lists ?? []).ToDictionary(x => (x.OwnerUsername, x.Name), x => listLookup.FirstOrDefault(l => l.Name == x.Name && l.OwnerUserId == MapUser(x.OwnerUsername))?.Id);

        var taskMap = new Dictionary<int, int>();
        var pending = (payload.Tasks ?? []).ToList();
        int attempts = pending.Count + 1;
        while (pending.Count > 0 && attempts-- > 0)
        {
            var progressed = false;
            foreach (var t in pending.ToList())
            {
                if (t.ParentTaskId.HasValue && !taskMap.ContainsKey(t.ParentTaskId.Value)) continue;
                var createdBy = MapUser(t.CreatedByUsername);
                if (createdBy == 0) continue;
                int? assigned = string.IsNullOrWhiteSpace(t.AssignedUsername) ? null : MapUser(t.AssignedUsername);
                int? categoryId = categoryByName.TryGetValue(t.CategoryName ?? "", out var foundCategoryId) ? foundCategoryId : null;
                int? listId = null;
                if (!string.IsNullOrWhiteSpace(t.ListName) && listMap.TryGetValue((t.ListOwnerUsername ?? t.CreatedByUsername, t.ListName), out var mappedList)) listId = mappedList;
                var item = new TaskItem
                {
                    RecordType = t.RecordType, Title = t.Title, Description = t.Description, Status = t.Status, Priority = t.Priority,
                    WorkDate = t.WorkDate, DurationHours = t.DurationHours, DueDate = t.DueDate, IsStarred = t.IsStarred, SortOrder = t.SortOrder,
                    RecurrenceType = t.RecurrenceType, ReminderAt = t.ReminderAt, ParentTaskId = t.ParentTaskId.HasValue ? taskMap[t.ParentTaskId.Value] : null,
                    TaskListId = listId, CategoryId = categoryId, CreatedById = createdBy, AssignedToId = assigned, CreatedAt = t.CreatedAt,
                    IsDeleted = t.IsDeleted, DeletedAt = t.DeletedAt, DeletedById = string.IsNullOrWhiteSpace(t.DeletedByUsername) ? null : MapUser(t.DeletedByUsername)
                };
                db.TaskItems.Add(item); await db.SaveChangesAsync(); taskMap[t.OldId] = item.Id; pending.Remove(t); progressed = true;
            }
            if (!progressed) break;
        }

        foreach (var c in payload.Comments ?? [])
            if (taskMap.TryGetValue(c.OldTaskId, out var newTaskId)) db.TaskComments.Add(new TaskComment { TaskItemId = newTaskId, UserId = MapUser(c.Username), Body = c.Body, CreatedAt = c.CreatedAt });

        var attachmentFolder = Path.Combine(env.ContentRootPath, "App_Data", "TaskAttachments"); Directory.CreateDirectory(attachmentFolder);
        foreach (var a in payload.Attachments ?? [])
        {
            if (!taskMap.TryGetValue(a.OldTaskId, out var newTaskId)) continue;
            var stored = Guid.NewGuid().ToString("N") + Path.GetExtension(a.OriginalFileName).ToLowerInvariant();
            var fileEntry = zip.GetEntry("attachments/" + a.StoredFileName);
            if (fileEntry is not null)
            {
                var path = Path.Combine(attachmentFolder, stored);
                await using var src = fileEntry.Open(); await using var dest = System.IO.File.Create(path); await src.CopyToAsync(dest);
            }
            else continue;
            db.TaskAttachments.Add(new TaskAttachment { TaskItemId = newTaskId, UploadedById = MapUser(a.UploadedByUsername), OriginalFileName = Path.GetFileName(a.OriginalFileName), StoredFileName = stored, ContentType = a.ContentType, SizeBytes = a.SizeBytes, UploadedAt = a.UploadedAt });
        }
        foreach (var a in payload.AuditLogs ?? []) db.AuditLogs.Add(new AuditLog { ActorUserId = string.IsNullOrWhiteSpace(a.ActorUsername) ? null : MapUser(a.ActorUsername), ActorName = a.ActorName, Action = a.Action, EntityType = a.EntityType, EntityId = a.EntityId, Details = a.Details, IpAddress = a.IpAddress, CreatedAt = a.CreatedAt });
        AuditWriter.Add(db, User, HttpContext, "بازیابی نسخه پشتیبان", "Backup", null, $"وظایف واردشده: {taskMap.Count}");
        await db.SaveChangesAsync();
        TempData["Message"] = $"بازیابی انجام شد. {taskMap.Count} وظیفه و پیگیری‌ها و پیوست‌های قابل بازیابی وارد شدند. اطلاعات موجود حذف نشدند.";
        return RedirectToPage();
    }

    public class BackupPayload { public DateTime CreatedAt { get; set; } public List<CategoryDto>? Categories { get; set; } public List<ListDto>? Lists { get; set; } public List<TaskDto>? Tasks { get; set; } public List<CommentDto>? Comments { get; set; } public List<AttachmentDto>? Attachments { get; set; } public List<AuditDto>? AuditLogs { get; set; } }
    public record CategoryDto(string Name, string? Description);
    public record ListDto(string Name, string OwnerUsername, DateTime CreatedAt);
    public record TaskDto(int OldId, TaskRecordType RecordType, string Title, string? Description, TaskStatus Status, TaskPriority Priority, DateTime WorkDate, decimal DurationHours, DateTime? DueDate, bool IsStarred, int SortOrder, RecurrenceType RecurrenceType, DateTime? ReminderAt, int? ParentTaskId, int? TaskListId, int? CategoryId, string CreatedByUsername, string? AssignedUsername, DateTime CreatedAt, bool IsDeleted, DateTime? DeletedAt, string? DeletedByUsername)
    { public string? CategoryName { get; set; } public string? ListName { get; set; } public string? ListOwnerUsername { get; set; } }
    public record CommentDto(int OldTaskId, string Username, string Body, DateTime CreatedAt);
    public record AttachmentDto(int OldTaskId, string UploadedByUsername, string OriginalFileName, string StoredFileName, string ContentType, long SizeBytes, DateTime UploadedAt);
    public record AuditDto(string? ActorUsername, string ActorName, string Action, string EntityType, int? EntityId, string? Details, string? IpAddress, DateTime CreatedAt);
}
