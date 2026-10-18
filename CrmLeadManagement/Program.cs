using CrmLeadManagement.Models;
using CrmLeadManagement.Models.Dtos;
using CrmLeadManagement.Filters;
using CrmLeadManagement.Security;
using CrmLeadManagement.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// Render sets the PORT environment variable (default 10000).
// When PORT is set (Render/Docker), bind to 0.0.0.0:{port} so reverse proxies can route traffic.
// In local development, bind to localhost:5000 so browsers (Chrome/Edge) can open the site without ERR_ADDRESS_INVALID.
var port = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrEmpty(port))
{
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
}
else
{
    builder.WebHost.UseUrls("http://localhost:5000");
}

// Forwarded headers for reverse proxies (Render terminates HTTPS in front of the container)
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

// Database connection resolution:
// Strategy & Priority:
// 1. Prefer 'ConnectionStrings:DefaultConnection' (from appsettings, secrets, or 'ConnectionStrings__DefaultConnection' env var)
// 2. Otherwise, read 'DATABASE_URL' (from Render, Docker, Heroku, or environment)
// 3. In non-production environments: fallback to local PostgreSQL (127.0.0.1:5432)

string? connectionString = null;
string connectionSource = "Not configured";

// 1. Check ConnectionStrings:DefaultConnection (supports appsettings, env vars via ConnectionStrings__DefaultConnection)
var configConn = builder.Configuration.GetConnectionString("DefaultConnection")
              ?? Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
              ?? Environment.GetEnvironmentVariable("ConnectionStrings:DefaultConnection");

if (!string.IsNullOrWhiteSpace(configConn) && !configConn.Contains("trusted_connection", StringComparison.OrdinalIgnoreCase))
{
    connectionString = ConnectionStringHelper.FromUrl(configConn);
    connectionSource = "Configuration 'ConnectionStrings:DefaultConnection'";
}
else
{
    // 2. Otherwise, check DATABASE_URL
    var databaseUrl = builder.Configuration["DATABASE_URL"]
                   ?? Environment.GetEnvironmentVariable("DATABASE_URL");

    if (!string.IsNullOrWhiteSpace(databaseUrl))
    {
        connectionString = ConnectionStringHelper.FromUrl(databaseUrl);
        connectionSource = "Environment variable 'DATABASE_URL'";
    }
}

// 3. Fallback or validate
if (string.IsNullOrWhiteSpace(connectionString))
{
    if (builder.Environment.IsProduction())
    {
        // Safe startup validation: Never log secrets, report checked keys and clear instructions
        throw new InvalidOperationException(
            "CRITICAL: Database connection string is not configured for Production.\n" +
            "Checked the following configuration sources (all were missing or empty):\n" +
            "  1. Configuration 'ConnectionStrings:DefaultConnection' (or env var 'ConnectionStrings__DefaultConnection')\n" +
            "  2. Environment variable 'DATABASE_URL'\n\n" +
            "To resolve this in the Render dashboard:\n" +
            "  1. Open your Render dashboard and navigate to your Web Service.\n" +
            "  2. Go to the 'Environment' tab.\n" +
            "  3. Set 'DATABASE_URL' to your PostgreSQL connection string (Internal Database URL if database is on Render),\n" +
            "     or set 'ConnectionStrings__DefaultConnection' with your Npgsql connection string.\n" +
            "  4. Click 'Save changes' to trigger a redeploy.");
    }

    // Development fallback
    connectionString = "Host=127.0.0.1;Port=5432;Database=crm;Username=postgres;Password=postgres;SSL Mode=Prefer;";
    connectionSource = "Local development fallback (127.0.0.1:5432)";
}

// Check for Render internal hostname used outside Render's private network
if (builder.Environment.IsDevelopment())
{
    try
    {
        var csb = new Npgsql.NpgsqlConnectionStringBuilder(connectionString);
        if (ConnectionStringHelper.IsRenderInternalHost(csb.Host))
        {
            Console.WriteLine(
                $"[WARNING] Detected Render internal hostname '{csb.Host}' while running in Development. " +
                "Render internal hostnames are only reachable from within Render's private network. " +
                "For local development connecting to Render PostgreSQL, use the External Database URL instead.");
        }
    }
    catch { /* Ignore parsing check in development */ }
}

builder.Services.AddDbContext<AppDbContext>(options =>
{
    options.UseNpgsql(connectionString);
    options.ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning));
});

// Lets the static LeadStore/SettingsStore classes reach the current request's
// AppDbContext without changing every controller's constructor — see DbAccessor.cs.
builder.Services.AddHttpContextAccessor();

// Authentication: Cookie scheme for browser sessions + Bearer scheme for API/mobile
builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.AddScoped<IAuditLogService, AuditLogService>();
builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.DefaultSignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.DefaultAuthenticateScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    })
    .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, options =>
    {
        options.LoginPath = "/login";
        options.LogoutPath = "/logout";
        options.AccessDeniedPath = "/login";
        options.ExpireTimeSpan = TimeSpan.FromHours(12);
        options.SlidingExpiration = true;
        options.Cookie.Name = "NimbusCRM.Auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Events.OnRedirectToLogin = context =>
        {
            if (context.Request.Path.StartsWithSegments("/api"))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                context.Response.ContentType = "application/json";
                return context.Response.WriteAsync("{\"Message\":\"Unauthorized\"}");
            }
            context.Response.Redirect(context.RedirectUri);
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = context =>
        {
            if (context.Request.Path.StartsWithSegments("/api"))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                context.Response.ContentType = "application/json";
                return context.Response.WriteAsync("{\"Message\":\"Forbidden\"}");
            }
            context.Response.Redirect(context.RedirectUri);
            return Task.CompletedTask;
        };
    })
    .AddScheme<AuthenticationSchemeOptions, BearerTokenAuthenticationHandler>(BearerTokenAuthenticationHandler.SchemeName, null);

// Cleanup expired/revoked access tokens older than 7 days daily
builder.Services.AddHostedService<TokenCleanupBackgroundService>();

// Rate limiting for auth endpoints (10 requests/minute per IP)
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, token) =>
    {
        context.HttpContext.Response.ContentType = "application/json";
        var error = new ApiError { Message = "Too many requests. Please try again later." };
        await context.HttpContext.Response.WriteAsync(System.Text.Json.JsonSerializer.Serialize(error), cancellationToken: token);
    };
    options.AddPolicy("AuthRateLimit", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            }));
});

builder.Services.AddControllersWithViews(options =>
    {
        // Saves tracked changes before the response starts (see Filters/SaveChangesFilter.cs).
        options.Filters.Add<SaveChangesFilter>();
    })
    .AddJsonOptions(options =>
    {
        // Keep API responses readable (PascalCase already matches the model properties,
        // this just avoids surprises if a client sends camelCase — it's accepted either way).
        options.JsonSerializerOptions.PropertyNamingPolicy = null;
        options.JsonSerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;
    });

// CORS: named policy reading Cors:AllowedOrigins in Production; allow any origin in Development
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();
builder.Services.AddCors(options =>
{
    options.AddPolicy("CorsPolicy", policy =>
    {
        if (builder.Environment.IsDevelopment())
        {
            policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
        }
        else if (allowedOrigins.Length > 0)
        {
            policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod().AllowCredentials();
        }
    });
});

// Swagger / OpenAPI with Bearer authentication
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "CRM Lead Management API",
        Version = "v1",
        Description = "JSON REST API for the Leads, Settings, and Auth modules with Bearer token authentication."
    });

    // Add Bearer Http security definition (user enters only the token)
    options.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
        Scheme = "bearer",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Description = "Enter your opaque bearer token (without the 'Bearer ' prefix)."
    });

    options.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
    {
        {
            new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Reference = new Microsoft.OpenApi.Models.OpenApiReference
                {
                    Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });

    options.CustomSchemaIds(type => type.FullName);
    options.ResolveConflictingActions(apiDescriptions => apiDescriptions.First());

    var xmlFile = $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    if (File.Exists(xmlPath))
    {
        options.IncludeXmlComments(xmlPath);
    }
});

var app = builder.Build();

// Forwarded headers MUST run first in the pipeline for correct reverse proxy IP/scheme detection
app.UseForwardedHeaders();

// Wire up DbAccessor so LeadStore/SettingsStore can find the current request's DbContext.
DbAccessor.Initialize(app.Services.GetRequiredService<IHttpContextAccessor>());

// Resolve uploads storage path (Storage:UploadsPath or default to wwwroot/uploads)
var uploadsPath = builder.Configuration["Storage:UploadsPath"];
if (string.IsNullOrWhiteSpace(uploadsPath))
{
    var webRoot = app.Environment.WebRootPath;
    if (string.IsNullOrWhiteSpace(webRoot))
    {
        webRoot = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
    }
    uploadsPath = Path.Combine(webRoot, "uploads");
}

if (!Directory.Exists(uploadsPath))
{
    Directory.CreateDirectory(uploadsPath);
}

// Set uploads path on attachment services
FinanceAttachmentService.UploadsPath = uploadsPath;
WorkAttachmentService.UploadsPath = uploadsPath;

// Applies pending EF Core migrations on startup, then seeds initial data if tables are empty.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var startupLogger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("DatabaseStartup");

    try
    {
        var dbConn = db.Database.GetDbConnection();
        startupLogger.LogInformation("Connecting to database '{Database}' on server '{DataSource}' (resolved via {ConnectionSource})...",
            dbConn.Database, dbConn.DataSource, connectionSource);
        startupLogger.LogInformation("Database configuration: {SafeDescription}",
            ConnectionStringHelper.GetSafeConnectionDescription(connectionString));

        // If the database already contains tables (e.g. from schema.sql or an earlier migration name),
        // baseline __EFMigrationsHistory with the current initial migration so Migrate() won't attempt
        // to recreate existing tables and throw PostgresException 42P07.
        BaselineExistingDatabaseIfNeeded(dbConn, startupLogger);

        startupLogger.LogInformation("Applying database migrations...");
        try
        {
            db.Database.Migrate();
            startupLogger.LogInformation("Database migrations applied successfully.");
        }
        catch (Exception migEx) when (IsRelationAlreadyExistsException(migEx))
        {
            startupLogger.LogWarning(
                "One or more database tables already exist in PostgreSQL. " +
                "Marking initial migration '20261015071435_InitialPg' as applied in '__EFMigrationsHistory' and continuing startup...");
            EnsureMigrationMarked(dbConn, "20261015071435_InitialPg", startupLogger);
        }

        // Ensure all required tables, columns, and indexes exist in PostgreSQL (even if migration was skipped due to baseline)
        DatabaseSchemaSync.EnsureAllTablesExist(dbConn, startupLogger);

        LeadStore.SeedIfEmpty(db);
        SettingsStore.SeedIfEmpty(db, builder.Configuration, startupLogger);
        RoleStore.SeedIfEmpty(db);

        if (app.Environment.IsProduction())
        {
            var hasAdmin = db.Users.Any(u => u.Role == "Admin");
            var hasSeedCreds = !string.IsNullOrWhiteSpace(builder.Configuration["Seed:AdminEmail"]) &&
                               !string.IsNullOrWhiteSpace(builder.Configuration["Seed:AdminPassword"]);
            if (!hasAdmin && !hasSeedCreds)
            {
                startupLogger.LogWarning("WARNING: No administrator account exists in the database and no Seed credentials ('Seed:AdminEmail', 'Seed:AdminPassword') are configured. Administrator login will not be possible until Seed:AdminPassword is provided or an admin is manually created.");
            }
        }
    }
    catch (Exception ex)
    {
        var pgEx = ex as Npgsql.PostgresException
                ?? ex.InnerException as Npgsql.PostgresException;
        var npgsqlEx = ex as Npgsql.NpgsqlException
                    ?? ex.InnerException as Npgsql.NpgsqlException;
        var socketEx = (ex as System.Net.Sockets.SocketException)
                    ?? (ex.InnerException as System.Net.Sockets.SocketException)
                    ?? (npgsqlEx?.InnerException as System.Net.Sockets.SocketException);

        if (socketEx != null && socketEx.SocketErrorCode == System.Net.Sockets.SocketError.ConnectionRefused)
        {
            var dbConn = db.Database.GetDbConnection();
            startupLogger.LogCritical(
                "Failed to connect to PostgreSQL server at '{DataSource}'. Connection refused (SocketError 10061). " +
                "PostgreSQL is not running on port 5432. Please ensure the PostgreSQL server or container is running, " +
                "or configure the target database via 'DATABASE_URL' or 'ConnectionStrings:DefaultConnection'.",
                dbConn.DataSource);
        }
        else if (pgEx?.SqlState == "42P07")
        {
            startupLogger.LogCritical(
                "Database table already exists ({Message}). Ensure '__EFMigrationsHistory' has recorded '20261015071435_InitialPg' or run: " +
                "INSERT INTO \"__EFMigrationsHistory\" (\"MigrationId\", \"ProductVersion\") VALUES ('20261015071435_InitialPg', '9.0.4') ON CONFLICT DO NOTHING;",
                pgEx.MessageText);
        }
        else
        {
            startupLogger.LogCritical(ex, "Database migration or seeding failed. Stopping application startup.");
        }
        throw;
    }
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

// Swagger UI + swagger.json: exposed in Development or if explicitly enabled via config "Swagger:Enabled" = true
var swaggerEnabled = app.Environment.IsDevelopment() || builder.Configuration.GetValue<bool>("Swagger:Enabled");
if (swaggerEnabled)
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "CRM Lead Management API v1");
    });
}

// Health check endpoint (does not touch the database, reachable anonymously)
app.MapGet("/health", () => "OK").AllowAnonymous();

// Serve standard wwwroot files
app.UseStaticFiles();

// Serve uploads folder at /uploads so stored links remain functional even when mapped to a persistent volume
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(uploadsPath),
    RequestPath = "/uploads"
});

app.UseRouting();
app.UseCors("CorsPolicy");
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();

static void BaselineExistingDatabaseIfNeeded(System.Data.Common.DbConnection dbConn, ILogger logger)
{
    try
    {
        if (dbConn.State != System.Data.ConnectionState.Open)
        {
            dbConn.Open();
        }

        using var cmd = dbConn.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS ""__EFMigrationsHistory"" (
                ""MigrationId"" character varying(150) NOT NULL,
                ""ProductVersion"" character varying(32) NOT NULL,
                CONSTRAINT ""PK___EFMigrationsHistory"" PRIMARY KEY (""MigrationId"")
            );

            DO $$
            BEGIN
                IF EXISTS (
                    SELECT 1 FROM pg_class c
                    JOIN pg_namespace n ON n.oid = c.relnamespace
                    WHERE c.relname ILIKE 'FollowUpSettings'
                ) OR EXISTS (
                    SELECT 1 FROM information_schema.tables
                    WHERE lower(table_name) = 'followupsettings'
                ) THEN
                    IF NOT EXISTS (SELECT 1 FROM ""__EFMigrationsHistory"" WHERE ""MigrationId"" = '20261015071435_InitialPg') THEN
                        INSERT INTO ""__EFMigrationsHistory"" (""MigrationId"", ""ProductVersion"")
                        VALUES ('20261015071435_InitialPg', '9.0.4')
                        ON CONFLICT (""MigrationId"") DO NOTHING;
                    END IF;
                END IF;
            END $$;
        ";
        cmd.ExecuteNonQuery();
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Pre-migration baseline check encountered an issue; proceeding with standard migration.");
    }
}

static void EnsureMigrationMarked(System.Data.Common.DbConnection dbConn, string migrationId, ILogger logger)
{
    try
    {
        if (dbConn.State != System.Data.ConnectionState.Open)
        {
            dbConn.Open();
        }

        using var cmd = dbConn.CreateCommand();
        cmd.CommandText = $@"
            CREATE TABLE IF NOT EXISTS ""__EFMigrationsHistory"" (
                ""MigrationId"" character varying(150) NOT NULL,
                ""ProductVersion"" character varying(32) NOT NULL,
                CONSTRAINT ""PK___EFMigrationsHistory"" PRIMARY KEY (""MigrationId"")
            );

            INSERT INTO ""__EFMigrationsHistory"" (""MigrationId"", ""ProductVersion"")
            VALUES ('{migrationId}', '9.0.4')
            ON CONFLICT (""MigrationId"") DO NOTHING;

            DELETE FROM ""__EFMigrationsHistory"" WHERE ""MigrationId"" = '20261013120000_InitialPostgres';
        ";
        cmd.ExecuteNonQuery();
        logger.LogInformation("Successfully baselined migration '{MigrationId}' in '__EFMigrationsHistory'.", migrationId);
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Failed to mark migration '{MigrationId}' in history table.", migrationId);
    }
}

static bool IsRelationAlreadyExistsException(Exception ex)
{
    var npgsqlEx = ex as Npgsql.NpgsqlException ?? ex.InnerException as Npgsql.NpgsqlException;
    if (npgsqlEx is Npgsql.PostgresException pgEx && pgEx.SqlState == "42P07")
    {
        return true;
    }
    if (ex.Message.Contains("already exists", StringComparison.OrdinalIgnoreCase) &&
        (ex.Message.Contains("relation", StringComparison.OrdinalIgnoreCase) || ex.Message.Contains("table", StringComparison.OrdinalIgnoreCase)))
    {
        return true;
    }
    return false;
}
