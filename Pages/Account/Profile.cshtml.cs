using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using TasksWebApp.Data;
using TasksWebApp.Helpers;
using TasksWebApp.Models;

namespace TasksWebApp.Pages.Account;

public class ProfileModel(AppDbContext db, IWebHostEnvironment environment) : PageModel
{
    private const long MaxPhotoBytes = 3 * 1024 * 1024;
    private string PhotoDirectory => Path.Combine(environment.ContentRootPath, "App_Data", "ProfilePhotos");

    public User? CurrentUser { get; private set; }
    public List<UserSession> Sessions { get; private set; } = [];
    public int TodoCount { get; private set; }
    public int WorkReportCount { get; private set; }
    public decimal TotalHours { get; private set; }
    public bool ForceChange { get; private set; }

    [BindProperty] public string CurrentPassword { get; set; } = string.Empty;
    [BindProperty] public string NewPassword { get; set; } = string.Empty;
    [BindProperty] public string ConfirmNewPassword { get; set; } = string.Empty;
    [BindProperty] public IFormFile? PhotoFile { get; set; }

    public async Task<IActionResult> OnGetAsync(bool forceChange = false)
    {
        ForceChange = forceChange;
        if (!await LoadCurrentUserAsync()) return Challenge();
        await LoadProfileExtrasAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostSignOutOtherSessionsAsync()
    {
        var user = await FindCurrentUserAsync();
        if (user is null) return Challenge();
        if (user.MustChangePassword) return RedirectToPage(new { forceChange = true });
        var currentSessionId = User.FindFirstValue("sid");
        var others = await db.UserSessions.Where(s => s.UserId == user.Id && s.RevokedAt == null && s.Id != currentSessionId).ToListAsync();
        foreach (var session in others) session.RevokedAt = DateTime.UtcNow;
        AuditWriter.Add(db, User, HttpContext, "خروج از سایر نشست‌ها", "UserSession", user.Id, $"تعداد نشست‌های خاتمه‌یافته: {others.Count}");
        await db.SaveChangesAsync();
        TempData["Success"] = $"{others.Count} نشست دیگر خاتمه یافت.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostChangePasswordAsync()
    {
        var user = await FindCurrentUserAsync();
        if (user is null) return Challenge();

        if (string.IsNullOrWhiteSpace(CurrentPassword) || !BCrypt.Net.BCrypt.Verify(CurrentPassword, user.PasswordHash))
            ModelState.AddModelError(nameof(CurrentPassword), "گذرواژه فعلی صحیح نیست.");
        if (string.IsNullOrWhiteSpace(NewPassword) || NewPassword.Length < 8)
            ModelState.AddModelError(nameof(NewPassword), "گذرواژه جدید باید حداقل ۸ نویسه داشته باشد.");
        if (NewPassword != ConfirmNewPassword)
            ModelState.AddModelError(nameof(ConfirmNewPassword), "گذرواژه جدید و تکرار آن یکسان نیستند.");
        if (!ModelState.IsValid)
        {
            await LoadCurrentUserAsync();
            await LoadProfileExtrasAsync();
            return Page();
        }

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(NewPassword);
        user.MustChangePassword = false;
        user.LastPasswordChangedAt = DateTime.UtcNow;
        var sessionId = User.FindFirstValue("sid");
        var sessions = await db.UserSessions.Where(x => x.UserId == user.Id && x.RevokedAt == null && x.Id != sessionId).ToListAsync();
        foreach (var session in sessions) session.RevokedAt = DateTime.UtcNow;
        AuditWriter.Add(db, User, HttpContext, "تغییر گذرواژه", "User", user.Id, "کاربر گذرواژه خود را تغییر داد.");
        await db.SaveChangesAsync();

        // پس از تغییر گذرواژه، نشست فعلی خاتمه می‌یابد و ورود دوباره لازم است.
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        TempData["PasswordChangedMessage"] = "گذرواژه با موفقیت تغییر کرد. برای ادامه، دوباره وارد سامانه شوید.";
        return RedirectToPage("/Account/Login");
    }

    public async Task<IActionResult> OnPostUploadPhotoAsync()
    {
        var user = await FindCurrentUserAsync();
        if (user is null) return Challenge();
        if (user.MustChangePassword) return RedirectToPage(new { forceChange = true });

        var extension = string.Empty;
        var contentType = string.Empty;
        if (PhotoFile is null || PhotoFile.Length == 0)
            ModelState.AddModelError(nameof(PhotoFile), "ابتدا یک تصویر انتخاب کن.");
        else if (PhotoFile.Length > MaxPhotoBytes)
            ModelState.AddModelError(nameof(PhotoFile), "حجم تصویر باید حداکثر ۳ مگابایت باشد.");
        else if (!TryGetImageType(PhotoFile, out extension, out contentType))
            ModelState.AddModelError(nameof(PhotoFile), "فقط تصاویر معتبر JPG، PNG یا WebP مجاز هستند.");

        if (!ModelState.IsValid)
        {
            await LoadCurrentUserAsync();
            await LoadProfileExtrasAsync();
            return Page();
        }

        // نام فایل تصادفی است و فایل خارج از wwwroot ذخیره می‌شود.
        Directory.CreateDirectory(PhotoDirectory);
        var fileName = $"{Guid.NewGuid():N}{extension}";
        var fullPath = Path.Combine(PhotoDirectory, fileName);
        await using (var stream = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            await PhotoFile!.CopyToAsync(stream);

        var oldFileName = user.ProfilePhotoFileName;
        user.ProfilePhotoFileName = fileName;
        AuditWriter.Add(db, User, HttpContext, "تغییر عکس پروفایل", "User", user.Id, $"نوع فایل: {contentType}");
        await db.SaveChangesAsync();
        TryDeletePhoto(oldFileName);
        TempData["Success"] = "عکس پروفایل با موفقیت به‌روزرسانی شد.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRemovePhotoAsync()
    {
        var user = await FindCurrentUserAsync();
        if (user is null) return Challenge();
        if (user.MustChangePassword) return RedirectToPage(new { forceChange = true });

        var oldFileName = user.ProfilePhotoFileName;
        user.ProfilePhotoFileName = null;
        AuditWriter.Add(db, User, HttpContext, "حذف عکس پروفایل", "User", user.Id, "کاربر عکس پروفایل را حذف کرد.");
        await db.SaveChangesAsync();
        TryDeletePhoto(oldFileName);
        TempData["Success"] = "عکس پروفایل حذف شد.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnGetPhotoAsync()
    {
        var user = await FindCurrentUserAsync();
        var fileName = user?.ProfilePhotoFileName;
        if (string.IsNullOrWhiteSpace(fileName) || Path.GetFileName(fileName) != fileName)
            return NotFound();

        var fullPath = Path.Combine(PhotoDirectory, fileName);
        if (!System.IO.File.Exists(fullPath)) return NotFound();

        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        var contentType = extension switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".webp" => "image/webp",
            _ => "application/octet-stream"
        };
        if (contentType == "application/octet-stream") return NotFound();

        Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
        Response.Headers.Pragma = "no-cache";
        return PhysicalFile(fullPath, contentType);
    }

    private async Task<User?> FindCurrentUserAsync()
    {
        var idValue = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(idValue, out var id)) return null;
        return await db.Users.SingleOrDefaultAsync(x => x.Id == id);
    }

    private async Task<bool> LoadCurrentUserAsync()
    {
        CurrentUser = await FindCurrentUserAsync();
        if (CurrentUser is not null) { ForceChange = CurrentUser.MustChangePassword; }
        return CurrentUser is not null;
    }

    private async Task LoadProfileExtrasAsync()
    {
        if (CurrentUser is null) return;
        var id = CurrentUser.Id;
        var records = db.TaskItems.AsNoTracking().Where(x => x.CreatedById == id && !x.IsDeleted);
        TodoCount = await records.CountAsync(x => x.RecordType == TaskRecordType.Todo);
        WorkReportCount = await records.CountAsync(x => x.RecordType == TaskRecordType.WorkReport);
        TotalHours = await records.Where(x => x.RecordType == TaskRecordType.WorkReport).SumAsync(x => (decimal?)x.DurationHours) ?? 0m;
        Sessions = await db.UserSessions.AsNoTracking().Where(x => x.UserId == id && x.RevokedAt == null && x.LastSeenAt >= DateTime.UtcNow.AddHours(-8))
            .OrderByDescending(x => x.LastSeenAt).Take(20).ToListAsync();
    }

    private static bool TryGetImageType(IFormFile file, out string extension, out string contentType)
    {
        extension = string.Empty;
        contentType = string.Empty;
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (ext is not (".jpg" or ".jpeg" or ".png" or ".webp")) return false;

        Span<byte> header = stackalloc byte[12];
        using var stream = file.OpenReadStream();
        var read = stream.Read(header);
        var valid = ext switch
        {
            ".jpg" or ".jpeg" => read >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF,
            ".png" => read >= 8 && header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47 && header[4] == 0x0D && header[5] == 0x0A && header[6] == 0x1A && header[7] == 0x0A,
            ".webp" => read >= 12 && header[0] == (byte)'R' && header[1] == (byte)'I' && header[2] == (byte)'F' && header[3] == (byte)'F' && header[8] == (byte)'W' && header[9] == (byte)'E' && header[10] == (byte)'B' && header[11] == (byte)'P',
            _ => false
        };
        if (!valid) return false;
        extension = ext == ".jpeg" ? ".jpg" : ext;
        contentType = ext is ".jpg" or ".jpeg" ? "image/jpeg" : ext == ".png" ? "image/png" : "image/webp";
        return true;
    }

    private void TryDeletePhoto(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) || Path.GetFileName(fileName) != fileName) return;
        var path = Path.Combine(PhotoDirectory, fileName);
        try { if (System.IO.File.Exists(path)) System.IO.File.Delete(path); }
        catch { /* حذف فایل قدیمی نباید موفقیت ذخیره اطلاعات را برگرداند. */ }
    }
}
