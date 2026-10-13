using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using TasksWebApp.Data;
using TasksWebApp.Models;

namespace TasksWebApp.Pages.Account;

public class RegisterModel(AppDbContext db) : PageModel
{
    [BindProperty, Required(ErrorMessage = "نام و نام خانوادگی الزامی است."), StringLength(150)]
    public string FullName { get; set; } = string.Empty;

    [BindProperty, Required(ErrorMessage = "شماره پرسنلی الزامی است."), StringLength(50)]
    public string EmployeeNumber { get; set; } = string.Empty;

    [BindProperty, Required(ErrorMessage = "نام کاربری الزامی است."), StringLength(100, MinimumLength = 3)]
    public string Username { get; set; } = string.Empty;

    [BindProperty, Required(ErrorMessage = "گذرواژه الزامی است."), MinLength(8, ErrorMessage = "گذرواژه باید حداقل ۸ نویسه باشد.")]
    public string Password { get; set; } = string.Empty;

    [BindProperty, Required(ErrorMessage = "تکرار گذرواژه الزامی است."), Compare(nameof(Password), ErrorMessage = "گذرواژه و تکرار آن یکسان نیستند.")]
    public string ConfirmPassword { get; set; } = string.Empty;

    public async Task<IActionResult> OnPostAsync()
    {
        FullName = FullName.Trim();
        EmployeeNumber = EmployeeNumber.Trim();
        Username = Username.Trim();

        if (!ModelState.IsValid)
            return Page();

        if (await db.Users.AnyAsync(x => x.Username == Username || x.EmployeeNumber == EmployeeNumber))
        {
            ModelState.AddModelError(string.Empty, "نام کاربری یا شماره پرسنلی قبلاً ثبت شده است.");
            return Page();
        }

        var user = new User
        {
            FullName = FullName,
            EmployeeNumber = EmployeeNumber,
            Username = Username,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(Password),
            Role = "User",
            CreatedAt = DateTime.UtcNow
        };

        db.Users.Add(user);
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Unique indexes also protect against two simultaneous registration requests.
            ModelState.AddModelError(string.Empty, "ثبت‌نام انجام نشد؛ نام کاربری یا شماره پرسنلی احتمالاً تکراری است.");
            return Page();
        }

        return RedirectToPage("Login");
    }
}
