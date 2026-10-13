using CrmLeadManagement.Models;
using CrmLeadManagement.Models.Dtos;
using CrmLeadManagement.Security;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CrmLeadManagement.Controllers.Api;

/// <summary>
/// JSON REST API for app settings (lead statuses, lead sources, users/roles, follow-up defaults).
/// Base route: /api/settings
/// </summary>
[ApiController]
[Route("api/settings")]
[Produces("application/json")]
[Authorize(AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme + "," + BearerTokenAuthenticationHandler.SchemeName)]
public class SettingsApiController : ControllerBase
{
    // GET /api/settings
    [HttpGet]
    public ActionResult<SettingsViewModel> GetAll()
    {
        return Ok(new SettingsViewModel
        {
            Statuses = SettingsStore.Statuses,
            Sources = SettingsStore.Sources,
            Users = SettingsStore.Users,
            Taxes = SettingsStore.Taxes,
            FollowUp = SettingsStore.FollowUp
        });
    }

    // --- Lead statuses ---------------------------------------------------

    // GET /api/settings/statuses
    [HttpGet("statuses")]
    public ActionResult<IEnumerable<string>> GetStatuses() => Ok(SettingsStore.Statuses);

    // POST /api/settings/statuses   { "value": "In Review" }
    [HttpPost("statuses")]
    public ActionResult<IEnumerable<string>> AddStatus([FromBody] LookupValueRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Value))
            return BadRequest(new ApiError { Message = "Value is required." });

        SettingsStore.AddStatus(request.Value);
        return Ok(SettingsStore.Statuses);
    }

    // DELETE /api/settings/statuses/{status}
    [HttpDelete("statuses/{status}")]
    public ActionResult<IEnumerable<string>> RemoveStatus(string status)
    {
        SettingsStore.RemoveStatus(status);
        return Ok(SettingsStore.Statuses);
    }

    // --- Lead sources ------------------------------------------------------

    // GET /api/settings/sources
    [HttpGet("sources")]
    public ActionResult<IEnumerable<string>> GetSources() => Ok(SettingsStore.Sources);

    // POST /api/settings/sources   { "value": "LinkedIn" }
    [HttpPost("sources")]
    public ActionResult<IEnumerable<string>> AddSource([FromBody] LookupValueRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Value))
            return BadRequest(new ApiError { Message = "Value is required." });

        SettingsStore.AddSource(request.Value);
        return Ok(SettingsStore.Sources);
    }

    // DELETE /api/settings/sources/{source}
    [HttpDelete("sources/{source}")]
    public ActionResult<IEnumerable<string>> RemoveSource(string source)
    {
        SettingsStore.RemoveSource(source);
        return Ok(SettingsStore.Sources);
    }

    // --- Users / roles -----------------------------------------------------

    // GET /api/settings/users
    [HttpGet("users")]
    public ActionResult<IEnumerable<UserRoleEntry>> GetUsers() => Ok(SettingsStore.Users);

    // POST /api/settings/users
    [HttpPost("users")]
    [Authorize(Roles = "Admin")]
    public ActionResult<IEnumerable<UserRoleEntry>> AddUser([FromBody] UserRoleEntry newUser)
    {
        if (string.IsNullOrWhiteSpace(newUser.Name) || string.IsNullOrWhiteSpace(newUser.Email))
            return BadRequest(new ApiError { Message = "Name and Email are required." });

        SettingsStore.AddUser(newUser);
        return Ok(SettingsStore.Users);
    }

    // DELETE /api/settings/users/{email}
    [HttpDelete("users/{email}")]
    [Authorize(Roles = "Admin")]
    public ActionResult<IEnumerable<UserRoleEntry>> RemoveUser(string email)
    {
        SettingsStore.RemoveUser(email);
        return Ok(SettingsStore.Users);
    }

    // --- Tax Master ---------------------------------------------------------

    // GET /api/settings/taxes
    [HttpGet("taxes")]
    public ActionResult<IEnumerable<TaxMaster>> GetTaxes() => Ok(SettingsStore.Taxes);

    // GET /api/settings/taxes/active
    [HttpGet("taxes/active")]
    public ActionResult<IEnumerable<TaxMaster>> GetActiveTaxes() => Ok(SettingsStore.ActiveTaxes);

    // POST /api/settings/taxes
    [HttpPost("taxes")]
    public ActionResult<IEnumerable<TaxMaster>> AddTax([FromBody] TaxMaster newTax)
    {
        if (string.IsNullOrWhiteSpace(newTax.TaxName))
            return BadRequest(new ApiError { Message = "Tax Name is required." });

        SettingsStore.AddTax(newTax);
        return Ok(SettingsStore.Taxes);
    }

    // PUT /api/settings/taxes/{id}
    [HttpPut("taxes/{id}")]
    public ActionResult<IEnumerable<TaxMaster>> UpdateTax(int id, [FromBody] TaxMaster tax)
    {
        tax.Id = id;
        SettingsStore.UpdateTax(tax);
        return Ok(SettingsStore.Taxes);
    }

    // DELETE /api/settings/taxes/{id}
    [HttpDelete("taxes/{id}")]
    public ActionResult<IEnumerable<TaxMaster>> DeleteTax(int id)
    {
        SettingsStore.DeleteTax(id);
        return Ok(SettingsStore.Taxes);
    }

    // POST /api/settings/taxes/{id}/toggle
    [HttpPost("taxes/{id}/toggle")]
    public ActionResult<IEnumerable<TaxMaster>> ToggleTax(int id)
    {
        SettingsStore.ToggleTaxStatus(id);
        return Ok(SettingsStore.Taxes);
    }

    // --- Follow-up defaults --------------------------------------------------

    // GET /api/settings/followup
    [HttpGet("followup")]
    public ActionResult<FollowUpSettings> GetFollowUp() => Ok(SettingsStore.FollowUp);

    // PUT /api/settings/followup
    [HttpPut("followup")]
    [Authorize(Roles = "Admin")]
    public ActionResult<FollowUpSettings> UpdateFollowUp([FromBody] FollowUpSettings request)
    {
        SettingsStore.UpdateFollowUpSettings(request);
        return Ok(SettingsStore.FollowUp);
    }
}
