using System.ComponentModel.DataAnnotations;
using DisputePortal.Infrastructure.Identity;
using DisputePortal.Web.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;

namespace DisputePortal.Web.Areas.Identity.Pages.Account;

/// <summary>
/// Replaces the stock Identity UI login page: no self-registration link (customers are onboarded by the
/// bank) and failed attempts count towards lockout, which the stock page leaves switched off.
/// </summary>
[AllowAnonymous]
[EnableRateLimiting(RateLimits.Authentication)]
public sealed class LoginModel(SignInManager<ApplicationUser> signInManager, ILogger<LoginModel> logger) : PageModel
{
    [BindProperty]
    public InputModel Input { get; set; } = new();

    public string? ReturnUrl { get; set; }

    public sealed class InputModel
    {
        [Required, EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required, DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;

        [Display(Name = "Keep me signed in on this device")]
        public bool RememberMe { get; set; }
    }

    public async Task OnGetAsync(string? returnUrl = null)
    {
        ReturnUrl = returnUrl;
        // Clear any half-finished external sign-in so we start clean.
        await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
    }

    public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
    {
        ReturnUrl = returnUrl;
        var target = Url.IsLocalUrl(returnUrl) ? returnUrl : Url.Content("~/");

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var result = await signInManager.PasswordSignInAsync(Input.Email, Input.Password, Input.RememberMe, lockoutOnFailure: true);

        if (result.Succeeded)
        {
            logger.LogInformation("User signed in");
            return LocalRedirect(target!);
        }

        if (result.RequiresTwoFactor)
        {
            return RedirectToPage("./LoginWith2fa", new { ReturnUrl = target, Input.RememberMe });
        }

        if (result.IsLockedOut)
        {
            logger.LogWarning("User account locked out");
            return RedirectToPage("./Lockout");
        }

        // Deliberately vague: don't reveal whether the email exists.
        ModelState.AddModelError(string.Empty, "That email and password combination didn't work. Please try again.");
        return Page();
    }
}
