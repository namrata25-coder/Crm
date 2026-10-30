using System.Security.Claims;
using CrmLeadManagement.Models;
using Microsoft.EntityFrameworkCore;

namespace CrmLeadManagement.Security;

public static class ProjectAccessPolicy
{
    public static async Task<HashSet<int>> GetAccessibleProjectIdsAsync(
        AppDbContext db,
        ClaimsPrincipal principal)
    {
        var projects = await db.WorkProjects
            .AsNoTracking()
            .Select(project => new WorkProject
            {
                Id = project.Id,
                ProjectManager = project.ProjectManager,
                AssignedMembers = project.AssignedMembers
            })
            .ToListAsync();

        return FilterAccessible(db, principal, projects)
            .Select(project => project.Id)
            .ToHashSet();
    }

    public static bool CanAccess(AppDbContext db, ClaimsPrincipal principal, WorkProject project)
    {
        var user = PermissionChecker.GetCurrentUser(db, principal);
        if (PermissionChecker.IsAdminOrManager(principal, user)) return true;
        if (user is null) return false;

        var identities = new[] { user.Name, user.Email, user.UserId }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim());

        return identities.Any(identity =>
            string.Equals(project.ProjectManager?.Trim(), identity, StringComparison.OrdinalIgnoreCase) ||
            (project.AssignedMembers ?? "")
                .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Any(member => string.Equals(member, identity, StringComparison.OrdinalIgnoreCase)));
    }

    public static List<WorkProject> FilterAccessible(
        AppDbContext db,
        ClaimsPrincipal principal,
        IEnumerable<WorkProject> projects)
    {
        var user = PermissionChecker.GetCurrentUser(db, principal);
        return FilterAccessible(db, principal, user, projects);
    }

    public static List<WorkProject> FilterAccessible(
        AppDbContext db,
        ClaimsPrincipal? principal,
        UserRoleEntry? user,
        IEnumerable<WorkProject> projects)
    {
        user ??= PermissionChecker.GetCurrentUser(db, principal);
        if (PermissionChecker.IsAdminOrManager(principal, user)) return projects.ToList();
        if (user is null) return new List<WorkProject>();

        var identities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(user.Name)) identities.Add(user.Name.Trim());
        if (!string.IsNullOrWhiteSpace(user.Email)) identities.Add(user.Email.Trim());
        if (!string.IsNullOrWhiteSpace(user.UserId)) identities.Add(user.UserId.Trim());

        if (principal?.Identity?.IsAuthenticated == true)
        {
            if (!string.IsNullOrWhiteSpace(principal.Identity.Name))
                identities.Add(principal.Identity.Name.Trim());

            var emailClaim = principal.FindFirst(ClaimTypes.Email)?.Value;
            if (!string.IsNullOrWhiteSpace(emailClaim))
                identities.Add(emailClaim.Trim());
        }

        return projects.Where(project =>
            (!string.IsNullOrWhiteSpace(project.ProjectManager) &&
             identities.Contains(project.ProjectManager.Trim())) ||
            (!string.IsNullOrWhiteSpace(project.AssignedMembers) &&
             project.AssignedMembers
                 .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                 .Any(identities.Contains))).ToList();
    }

    public static async Task<int> CountAccessibleProjectsAsync(
        AppDbContext db,
        ClaimsPrincipal? principal,
        UserRoleEntry? user = null)
    {
        user ??= PermissionChecker.GetCurrentUser(db, principal);
        if (PermissionChecker.IsAdminOrManager(principal, user))
        {
            return await db.WorkProjects.CountAsync();
        }
        if (user is null) return 0;

        var identities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(user.Name)) identities.Add(user.Name.Trim());
        if (!string.IsNullOrWhiteSpace(user.Email)) identities.Add(user.Email.Trim());
        if (!string.IsNullOrWhiteSpace(user.UserId)) identities.Add(user.UserId.Trim());

        if (principal?.Identity?.IsAuthenticated == true)
        {
            if (!string.IsNullOrWhiteSpace(principal.Identity.Name))
                identities.Add(principal.Identity.Name.Trim());

            var emailClaim = principal.FindFirst(ClaimTypes.Email)?.Value;
            if (!string.IsNullOrWhiteSpace(emailClaim))
                identities.Add(emailClaim.Trim());
        }

        if (identities.Count == 0) return 0;

        var projects = await db.WorkProjects
            .AsNoTracking()
            .Select(p => new
            {
                p.ProjectManager,
                p.AssignedMembers
            })
            .ToListAsync();

        return projects.Count(p =>
            (!string.IsNullOrWhiteSpace(p.ProjectManager) &&
             identities.Contains(p.ProjectManager.Trim())) ||
            (!string.IsNullOrWhiteSpace(p.AssignedMembers) &&
             p.AssignedMembers
                 .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                 .Any(identities.Contains)));
    }
}
