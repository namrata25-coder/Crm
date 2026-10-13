# NimbusCRM — Lead Management (ASP.NET Core MVC)

A production-styled ASP.NET Core **MVC** CRM Lead Management module (migrated
from Razor Pages — there is no `Pages/` folder or `PageModel` anywhere in the
project anymore; every screen is a Controller + View). Data lives in static
in-memory stores (`Models/LeadStore.cs` and `Models/SettingsStore.cs`) rather
than a database — there's no persistence across app restarts, but Add / Edit /
Delete are **real, working operations** against that in-memory store for the
lifetime of the running app: add a lead, refresh, edit it, refresh, delete
it — the changes stick.

## What's implemented

1. **Lead CRUD** — Add, Edit and Delete are available from both Lead List
   and Assigned List (pencil / trash icons next to View). Saves post to
   `LeadController.Save` / deletes post to `LeadController.Delete`, which
   call `LeadStore.AddLead` / `UpdateLead` / `DeleteLead`, then redirect back
   to whichever page the request came from (via a `returnUrl` field) so the
   grid reflects the change immediately.
2. **Call popup** — read-only Call History (Date, Time, Call Type, Employee,
   Duration, Status, Conversation/Notes) with dummy data and a single
   **Close** button.
3. **Follow-up popup** — read-only Follow-up details (Date, Time, Type,
   Assigned Employee, Status, Remarks) with a single **Close** button.
4. **Visit information** — Lead List has a **Visit Location** column showing
   the latest visit; Lead Details has a full **Visit History** panel (Visit
   Date, Time, Location, Salesperson, Conversation, Result/Outcome, Remarks)
   with dummy visits seeded on a few leads.
5. **Settings** (`/Settings`) — functional sections for Lead Status, Lead
   Sources, Users/Roles, and Follow-up Settings, all backed by
   `SettingsStore` and editable through the page (add/remove statuses and
   sources, add/remove users, save follow-up defaults). Status/Source/
   Assigned-To dropdowns throughout the app read from this store, so changes
   in Settings show up immediately in the Add/Edit Lead form.

Requirement and Assign/Reassign modals on Lead Details are unchanged, as are
all existing lead fields and the Lead List → View → Lead Details flow.

## Run it

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

```bash
cd CrmLeadManagement
dotnet run
```

Then open the URL shown in the console (typically `https://localhost:5001` or
`http://localhost:5000`) — it redirects straight to the Lead List.

## Routes

- **`/`** — `HomeController.Index` redirects to the Lead List.
- **`/Lead/List`** — Lead List with the full data table (Lead ID, Lead Name,
  Company, Mobile, Email, Status, Source, Assigned To, Follow-up Date,
  Actions). Only a **View** action is exposed here; it navigates to
  `/Lead/Details/LD-1001`.
- **`/Lead/Details/LD-1001`** — Full lead profile. Four action buttons
  (Call, Follow Up, Requirement, Assign/Reassign) open modals with history
  tables/timelines.
- **`/Lead/Assigned`** — Assigned leads with search, status filter, employee
  filter, date filter, pagination controls, View / Edit-Reassign actions,
  and an **Add Lead** button that opens the full Add Lead form in a modal.
- **`/Settings`** — Lead Status, Lead Sources, Users/Roles, Follow-up
  Settings.
- **`POST /Lead/Save`**, **`POST /Lead/Delete`** — shared by both list
  pages; each form carries a hidden `returnUrl` so the user lands back on
  the page they started from.

## Structure

```
Controllers/
  HomeController.cs         Root "/" -> redirects to Lead/List
  LeadController.cs         List, Assigned, Details, and the shared Save/Delete actions
  SettingsController.cs     Lead Status, Lead Sources, Users/Roles, Follow-up Settings actions
Views/
  _ViewImports.cshtml       @using CrmLeadManagement.Models + tag helpers, shared by all views
  _ViewStart.cshtml         Applies Shared/_Layout to every view
  Shared/_Layout.cshtml     Sidebar + top navigation shell
  Lead/List.cshtml          Lead List page: table + Visit Location column, Add/Edit/Delete
  Lead/Assigned.cshtml      Assigned List: search/status/employee/date filters, Add/Edit/Delete
  Lead/Details.cshtml       Lead Details: Call/Follow-up (read-only popups), Requirement,
                              Assign/Reassign modals, plus a Visit History panel
  Lead/_LeadFormModal.cshtml Shared Add/Edit Lead modal (partial, used by both list views)
  Settings/Index.cshtml     Lead Status, Lead Sources, Users/Roles, Follow-up Settings
Models/
  Lead.cs                    Lead + CallHistoryEntry/FollowUpEntry/RequirementEntry/VisitEntry
  LeadStore.cs                In-memory Lead data + AddLead/UpdateLead/DeleteLead (no DB)
  SettingsStore.cs            In-memory Settings data (statuses, sources, users, follow-up config)
  LeadInputModel.cs           Add/Edit Lead form binding model
  SettingsViewModel.cs        Bundles Statuses/Sources/Users/FollowUp for the Settings view
wwwroot/
  css/site.css               Design system (colors, type, tables, cards, modals) — unchanged
  js/site.js                 Modal helpers + Add/Edit Lead modal populate/reset logic — unchanged
```

## JSON REST API (for a separate frontend / backend consumption)

In addition to the MVC pages, this project now exposes a plain JSON REST API
under `/api`, backed by the same in-memory `LeadStore` / `SettingsStore` used
by the views (so data added via the API shows up on the pages, and vice
versa). CORS is enabled for all origins in `Program.cs` so any frontend
(React, Angular, mobile app, Postman, etc.) can call it directly — tighten the
`AllowAll` CORS policy before deploying to production.

### Leads — `/api/leads`

| Method | Route | Description |
|---|---|---|
| GET | `/api/leads` | List leads. Optional query params: `status`, `assignedTo`, `search` |
| GET | `/api/leads/assigned` | Leads that currently have an `AssignedTo` |
| GET | `/api/leads/{id}` | Get one lead by id (e.g. `LD-1001`) |
| POST | `/api/leads` | Create a lead (JSON body: `LeadInputModel`) |
| PUT | `/api/leads/{id}` | Update a lead |
| DELETE | `/api/leads/{id}` | Delete a lead |
| POST | `/api/leads/{id}/calls` | Log a call (`AddCallRequest`) |
| POST | `/api/leads/{id}/followups` | Log a follow-up (`AddFollowUpRequest`) |
| POST | `/api/leads/{id}/requirements` | Add a requirement, JSON only, no image (`AddRequirementRequest`) |
| POST | `/api/leads/{id}/requirements/upload` | Add a requirement with an optional image (`multipart/form-data`) |
| POST | `/api/leads/{id}/assign` | Assign / re-assign the lead (`AssignLeadRequest`) |

Example — create a lead (requires Bearer token or session cookie; unauthenticated requests return 401 Unauthorized):

```bash
curl -X POST http://localhost:5000/api/leads \
  -H "Authorization: Bearer <your_access_token>" \
  -H "Content-Type: application/json" \
  -d '{
        "leadName": "Test User",
        "companyName": "Test Co",
        "mobileNumber": "+91 90000 00000",
        "email": "test@test.com",
        "source": "Website",
        "leadStatus": "New",
        "assignedTo": "Ananya Sharma",
        "priority": "Medium"
      }'
```

### Settings — `/api/settings`

| Method | Route | Description |
|---|---|---|
| GET | `/api/settings` | Everything (statuses, sources, users, follow-up defaults) |
| GET / POST | `/api/settings/statuses` | List / add a lead status. POST body: `{ "value": "In Review" }` |
| DELETE | `/api/settings/statuses/{status}` | Remove a status |
| GET / POST | `/api/settings/sources` | List / add a lead source. POST body: `{ "value": "LinkedIn" }` |
| DELETE | `/api/settings/sources/{source}` | Remove a source |
| GET / POST | `/api/settings/users` | List / add a user (`UserRoleEntry`) |
| DELETE | `/api/settings/users/{email}` | Remove a user |
| GET / PUT | `/api/settings/followup` | Read / update follow-up defaults (`FollowUpSettings`) |

### Running it

```bash
cd CrmLeadManagement
dotnet restore
dotnet run
```

The MVC pages stay at `http://localhost:5000/` and the API sits alongside at
`http://localhost:5000/api/...` — same process, same port, no extra setup.
New API code lives in `Controllers/Api/` and `Models/Dtos/`; nothing in the
existing controllers, views, or models was changed.

## Bearer Token Authentication

The REST API is secured with plain opaque Bearer token authentication (no JWT). Tokens are 32 cryptographically random bytes generated via `RandomNumberGenerator`, Base64Url encoded, and hashed with SHA-256 before storage in the database table `AccessTokens`.

> **Security Notice**: Tokens must only travel over **HTTPS** in production environments to prevent interception.

### Configuration Keys (`appsettings.json`)

| Key | Default | Description |
|---|---|---|
| `Seed:AdminEmail` | `"admin@nimbuscrm.com"` | Initial administrator email address. |
| `Seed:AdminPassword` | `"AdminPassword123!"` | Initial administrator password hashed with `PasswordHasher<UserRoleEntry>`. |
| `BearerAuth:TokenHours` | `12` | Base lifetime of issued access tokens in hours. |
| `BearerAuth:SlidingExpiration` | `true` | If true, extends token expiry when used and less than half of lifetime remains. |
| `BearerAuth:MaxDays` | `30` | Maximum absolute lifetime cap for sliding token refresh in days. |
| `Auth:RegistrationMode` | `"Disabled"` | Registration policy: `"Disabled"`, `"Open"`, or `"ApprovalRequired"` (creates inactive user). |
| `Cors:AllowedOrigins` | `["http://localhost:3000", ...]` | Allowed CORS origins in non-Development environments. |

### Creating the First Administrator

When the application starts, `SettingsStore.SeedIfEmpty(db, Configuration)` reads `Seed:AdminEmail` and `Seed:AdminPassword`. If the administrator does not exist, an `Admin` user is created with a password hashed via ASP.NET Core's `PasswordHasher<UserRoleEntry>`.

To customize the initial credentials, configure them in `appsettings.json` (or environment variables / secrets) before the first launch:

```json
"Seed": {
  "AdminEmail": "admin@yourdomain.com",
  "AdminPassword": "YourStrongPassword123!"
}
```

### Running EF Core Migrations

Migrations are automatically applied on startup via `db.Database.Migrate()`. To manage migrations from the CLI:

```bash
# Add a new migration
dotnet ef migrations add <MigrationName>

# Apply pending migrations to the database
dotnet ef database update

# Remove last migration (if not yet applied to database)
dotnet ef migrations remove
```

### Using the Authorize Button in Swagger

1. Open `http://localhost:5000/swagger` in your browser.
2. Log in by calling `POST /api/auth/login` with your credentials:
   ```json
   {
     "email": "admin@nimbuscrm.com",
     "password": "AdminPassword123!"
   }
   ```
3. Copy the returned `Token` string from the 200 response.
4. Click the green **Authorize** button at the top right of the Swagger UI.
5. In the `Bearer (http, Bearer)` field, paste **only** the token (do not type `Bearer ` prefix — Swagger adds it automatically).
6. Click **Authorize**, then **Close**. All protected endpoints (`/api/leads`, `/api/settings`, `/api/auth/me`, etc.) will now include the Bearer token header on subsequent requests.

