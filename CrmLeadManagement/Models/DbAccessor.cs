namespace CrmLeadManagement.Models;

/// <summary>
/// LeadStore and SettingsStore are static classes (used all over the existing
/// controllers/views without a constructor), so they can't take AppDbContext via
/// normal constructor injection. This gives them a way to reach the current HTTP
/// request's DbContext instead — ASP.NET Core already creates one AppDbContext
/// per request (see AddDbContext in Program.cs); this just hands it to code that
/// isn't itself a controller.
///
/// HttpContextAccessor is assigned once, at startup, in Program.cs.
/// </summary>
public static class DbAccessor
{
    private static IHttpContextAccessor? _httpContextAccessor;

    public static void Initialize(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    /// <summary>The AppDbContext for the current HTTP request.</summary>
    public static AppDbContext Db
    {
        get
        {
            var context = _httpContextAccessor?.HttpContext
                ?? throw new InvalidOperationException(
                    "DbAccessor.Db was used outside an HTTP request. Did Program.cs call DbAccessor.Initialize(...)?");

            return (AppDbContext)context.RequestServices.GetService(typeof(AppDbContext))!;
        }
    }
}
