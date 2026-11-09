using System.ComponentModel.DataAnnotations;

namespace DisputePortal.Application.Common;

/// <summary>
/// Handlers re-check their input even though MVC/the API already did: the application layer is the trust
/// boundary, and it keeps every use case safe to call from anywhere (tests, jobs, a future UI).
/// </summary>
internal static class RequestValidator
{
    public static void Validate(object request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var results = new List<ValidationResult>();
        if (Validator.TryValidateObject(request, new ValidationContext(request), results, validateAllProperties: true))
        {
            return;
        }

        var errors = results
            .SelectMany(r => (r.MemberNames.Any() ? r.MemberNames : [string.Empty]).Select(m => (Member: m, r.ErrorMessage)))
            .GroupBy(x => x.Member)
            .ToDictionary(g => g.Key, g => g.Select(x => x.ErrorMessage ?? "Invalid value.").ToArray());

        throw new RequestValidationException(errors);
    }

    public static (int Page, int PageSize) NormalisePaging(int page, int pageSize) =>
        (Math.Max(1, page), Math.Clamp(pageSize, 1, 100));
}
