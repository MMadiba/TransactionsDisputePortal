using System.ComponentModel.DataAnnotations;
using DisputePortal.Application.Abstractions;
using DisputePortal.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace DisputePortal.Infrastructure.Identity;

/// <summary>ASP.NET Core Identity user. Kept out of the domain: "who can sign in" is not a banking concept.</summary>
public sealed class ApplicationUser : IdentityUser
{
    [PersonalData]
    [MaxLength(120)]
    public string FullName { get; set; } = string.Empty;
}

internal sealed class UserDirectory(ApplicationDbContext db) : IUserDirectory
{
    public Task<string?> GetDisplayNameAsync(string userId, CancellationToken cancellationToken = default) =>
        db.Users.Where(u => u.Id == userId).Select(u => u.FullName).SingleOrDefaultAsync(cancellationToken);
}
