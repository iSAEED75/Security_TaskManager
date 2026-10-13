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

public class LoginModel(AppDbContext db) : PageModel
{
    [BindProperty] public string Username { get; set; } = string.Empty;
    [BindProperty] public string Password { get; set; } = string.Empty;
    [BindProperty] public bool RememberMe { get; set; }

    public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
    {
        var username = Username?.Trim() ?? string.Empty;
        var user = await db.Users.SingleOrDefaultAsync(x => x.Username == username);
        var valid = false;
        if (user is not null && !string.IsNullOrEmpty(Password))
        {
            try { valid = BCrypt.Net.BCrypt.Verify(Password, user.PasswordHash); }
            catch { valid = false; }
        }

        // نسخه‌های قبلی سامانه ممکن است حساب پیش‌فرض را بدون پرچم اجبار تغییر گذرواژه داشته باشند.
        if (valid && user is not null && user.Role == "Admin" && user.Username == "admin" && !user.MustChangePassword)
        {
            try
            {
                if (BCrypt.Net.BCrypt.Verify("Admin@1234", user.PasswordHash)) user.MustChangePassword = true;
            }
            catch { /* در صورت هش نامعتبر، ورود پایین‌تر رد می‌شود یا بررسی عادی ادامه می‌یابد. */ }
        }

        if (!valid)
        {
            if (user is not null)
            {
                user.FailedLoginCount = Math.Min(user.FailedLoginCount + 1, 2_000_000_000);
                user.LastFailedLoginAt = DateTime.UtcNow;
                AuditWriter.Add(db, new ClaimsPrincipal(new ClaimsIdentity()), HttpContext, "ورود ناموفق", "Account", user.Id,
                    $"تلاش ناموفق برای نام کاربری: {user.Username}");
            }
            else
            {
                AuditWriter.Add(db, new ClaimsPrincipal(new ClaimsIdentity()), HttpContext, "ورود ناموفق", "Account", null,
                    $"نام کاربری واردشده: {(username.Length > 100 ? username[..100] : username)}");
            }
            await db.SaveChangesAsync();
            ModelState.AddModelError(string.Empty, "نام کاربری یا گذرواژه نادرست است.");
            return Page();
        }

        var now = DateTime.UtcNow;
        user!.LastLoginAt = now;
        user.FailedLoginCount = 0;
        var userAgent = Request.Headers["User-Agent"].ToString();
        var session = new UserSession
        {
            Id = Guid.NewGuid().ToString("N"), UserId = user.Id, CreatedAt = now, LastSeenAt = now,
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
            UserAgent = userAgent[..Math.Min(userAgent.Length, 500)]
        };
        db.UserSessions.Add(session);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.FullName),
            new(ClaimTypes.Role, user.Role),
            new("sid", session.Id),
            new("must_change_password", user.MustChangePassword ? "true" : "false")
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims));
        AuditWriter.Add(db, principal, HttpContext, "ورود موفق", "Account", user.Id, "ورود به سامانه");
        await db.SaveChangesAsync();

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var properties = new AuthenticationProperties { IsPersistent = RememberMe, AllowRefresh = true, IssuedUtc = now };
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity), properties);

        if (user.MustChangePassword) return RedirectToPage("/Account/Profile", new { forceChange = true });
        if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl)) return LocalRedirect(returnUrl);
        return RedirectToPage("/Tasks/Index");
    }
}
