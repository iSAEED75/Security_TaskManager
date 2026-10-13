using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
namespace TasksWebApp.Pages;
public class IndexModel:PageModel{public IActionResult OnGet()=>User.Identity?.IsAuthenticated==true?RedirectToPage("/Tasks/Index"):RedirectToPage("/Account/Login");}