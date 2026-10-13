using CrmLeadManagement.Models;
using CrmLeadManagement.Models.Dtos;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CrmLeadManagement.Filters;

/// <summary>
/// Commits tracked EF Core changes right after an action finishes but BEFORE its result
/// (view / JSON) is written to the client.
/// Automatically handles DB update exceptions, rolls back corrupted tracker state,
/// and returns clean API error responses or logs MVC errors.
/// </summary>
public class SaveChangesFilter : IAsyncActionFilter
{
    private const int UniqueConstraintViolation = 2601;   // duplicate key in unique index
    private const int DuplicateKeyViolation    = 2627;    // duplicate key in primary key / unique constraint
    private const int ForeignKeyViolation      = 547;     // FK / check constraint
    private const int StringTruncation         = 8152;    // string or binary data would be truncated
    private const int StringTruncationLegacy   = 2628;    // same, older SQL Server versions

    private readonly AppDbContext _db;
    private readonly ILogger<SaveChangesFilter> _logger;
    private readonly IHostEnvironment _env;

    public SaveChangesFilter(AppDbContext db, ILogger<SaveChangesFilter> logger, IHostEnvironment env)
    {
        _db = db;
        _logger = logger;
        _env = env;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var executed = await next();

        // If the action threw an unhandled exception, do not attempt to persist half-done work.
        if (executed.Exception is not null && !executed.ExceptionHandled)
        {
            _db.ChangeTracker.Clear();
            return;
        }

        // If the action already returned an error status code (>= 400), don't commit.
        if (executed.Result is IStatusCodeActionResult { StatusCode: >= 400 })
        {
            _db.ChangeTracker.Clear();
            return;
        }

        // Only save if there are pending tracked changes
        if (!_db.ChangeTracker.HasChanges()) return;

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "DbUpdateException occurred during SaveChangesAsync for {Method} {Path}.",
                context.HttpContext.Request.Method, context.HttpContext.Request.Path);

            _db.ChangeTracker.Clear();

            var isApi = context.HttpContext.Request.Path.StartsWithSegments("/api");
            if (isApi)
            {
                executed.Result = BuildDbUpdateErrorResult(ex);
            }
            else
            {
                executed.Result = new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during SaveChangesAsync for {Method} {Path}.",
                context.HttpContext.Request.Method, context.HttpContext.Request.Path);

            _db.ChangeTracker.Clear();

            var isApi = context.HttpContext.Request.Path.StartsWithSegments("/api");
            if (isApi)
            {
                executed.Result = new ObjectResult(new ApiError
                {
                    Message = "An unexpected error occurred while saving changes.",
                    Detail = _env.IsDevelopment() ? ex.Message : null
                })
                { StatusCode = StatusCodes.Status500InternalServerError };
            }
            else
            {
                executed.Result = new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }
        }
    }

    private ObjectResult BuildDbUpdateErrorResult(DbUpdateException ex)
    {
        var sqlEx = ex.InnerException as SqlException;
        var number = sqlEx?.Number ?? 0;
        var detail = _env.IsDevelopment() ? (sqlEx?.Message ?? ex.Message) : null;

        return number switch
        {
            UniqueConstraintViolation or DuplicateKeyViolation => new ObjectResult(new ApiError
            {
                Message = "A record with the same unique identifier already exists.",
                Detail = detail
            })
            { StatusCode = StatusCodes.Status409Conflict },

            ForeignKeyViolation => new ObjectResult(new ApiError
            {
                Message = "The operation violates a database constraint.",
                Detail = detail
            })
            { StatusCode = StatusCodes.Status409Conflict },

            StringTruncation or StringTruncationLegacy => new ObjectResult(new ApiError
            {
                Message = "One or more values exceed the maximum allowed length.",
                Detail = detail
            })
            { StatusCode = StatusCodes.Status400BadRequest },

            _ => new ObjectResult(new ApiError
            {
                Message = "The changes could not be saved to the database.",
                Detail = detail
            })
            { StatusCode = StatusCodes.Status500InternalServerError }
        };
    }
}