using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using TasksWebApp.Data;
using TasksWebApp.Helpers;
using TasksWebApp.Models;

namespace TasksWebApp.Pages.Admin;

[Authorize(Roles = "Admin")]
public class UsersModel(AppDbContext db) : PageModel
{
    public List<User> Users { get; private set; } = [];

    [BindProperty] public string FullName { get; set; } = string.Empty;
    [BindProperty] public string Username { get; set; } = string.Empty;
    [BindProperty] public string EmployeeNumber { get; set; } = string.Empty;
    [BindProperty] public string Role { get; set; } = "User";

    public async Task OnGetAsync() => await LoadUsersAsync();

    public async Task<IActionResult> OnPostCreateAsync()
    {
        FullName = (FullName ?? string.Empty).Trim();
        Username = (Username ?? string.Empty).Trim();
        EmployeeNumber = (EmployeeNumber ?? string.Empty).Trim();
        Role = Role == "Admin" ? "Admin" : "User";
        if (FullName.Length is < 2 or > 150) ModelState.AddModelError(nameof(FullName), "نام باید بین ۲ تا ۱۵۰ نویسه باشد.");
        if (Username.Length is < 3 or > 100 || Username.Any(char.IsWhiteSpace)) ModelState.AddModelError(nameof(Username), "نام کاربری باید ۳ تا ۱۰۰ نویسه و بدون فاصله باشد.");
        if (EmployeeNumber.Length is < 1 or > 50) ModelState.AddModelError(nameof(EmployeeNumber), "شماره پرسنلی الزامی است و حداکثر ۵۰ نویسه دارد.");
        if (!ModelState.IsValid) { await LoadUsersAsync(); return Page(); }
        if (await db.Users.AnyAsync(x => x.Username == Username || x.EmployeeNumber == EmployeeNumber))
        {
            ModelState.AddModelError(string.Empty, "نام کاربری یا شماره پرسنلی قبلاً ثبت شده است.");
            await LoadUsersAsync(); return Page();
        }

        var temporaryPassword = GenerateTemporaryPassword();
        var user = new User
        {
            FullName = FullName, Username = Username, EmployeeNumber = EmployeeNumber, Role = Role,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(temporaryPassword), CreatedAt = DateTime.UtcNow,
            MustChangePassword = true
        };
        db.Users.Add(user);
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateException)
        {
            ModelState.AddModelError(string.Empty, "ایجاد حساب انجام نشد؛ احتمالاً نام کاربری یا شماره پرسنلی تکراری است.");
            await LoadUsersAsync(); return Page();
        }
        AuditWriter.Add(db, User, HttpContext, "ایجاد حساب کاربری", "User", user.Id, $"حساب {user.Username} با الزام تغییر گذرواژه ایجاد شد.");
        await db.SaveChangesAsync();
        TempData["OneTimePassword"] = temporaryPassword;
        TempData["OneTimePasswordFor"] = user.Username;
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostResetPasswordAsync(int id)
    {
        var target = await db.Users.SingleOrDefaultAsync(x => x.Id == id);
        if (target is null) return NotFound();
        if (int.TryParse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out var currentUserId) && currentUserId == id)
        {
            TempData["AdminError"] = "برای بازنشانی گذرواژه حساب مدیر فعلی، از یک حساب مدیر دیگر استفاده کن تا نشست و گذرواژه موقت از دست نرود.";
            return RedirectToPage();
        }
        var temporaryPassword = GenerateTemporaryPassword();
        target.PasswordHash = BCrypt.Net.BCrypt.HashPassword(temporaryPassword);
        target.MustChangePassword = true;
        target.LastPasswordChangedAt = DateTime.UtcNow;
        var sessions = await db.UserSessions.Where(x => x.UserId == id && x.RevokedAt == null).ToListAsync();
        foreach (var session in sessions) session.RevokedAt = DateTime.UtcNow;
        AuditWriter.Add(db, User, HttpContext, "بازنشانی گذرواژه توسط مدیر", "User", id,
            $"گذرواژه موقت تولید شد؛ تعداد نشست‌های خاتمه‌یافته: {sessions.Count}. مقدار گذرواژه در گزارش ثبت نمی‌شود.");
        await db.SaveChangesAsync();
        TempData["OneTimePassword"] = temporaryPassword;
        TempData["OneTimePasswordFor"] = target.Username;
        return RedirectToPage();
    }

    private async Task LoadUsersAsync() => Users = await db.Users.AsNoTracking()
        .OrderBy(x => x.FullName).Take(1000).ToListAsync();

    private static string GenerateTemporaryPassword()
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789!@$%*-_";
        var bytes = RandomNumberGenerator.GetBytes(18);
        return new string(bytes.Select(b => alphabet[b % alphabet.Length]).ToArray());
    }
}
