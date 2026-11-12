using InventoryApp.Api.Data;
using InventoryApp.Api.Dtos;
using InventoryApp.Api.Models;
using InventoryApp.Api.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace InventoryApp.Api.Controllers
{
    /// <summary>
    /// A Platform Admin working inside one tenant (support): tenant settings, devices, employees, sessions
    /// and the tenant's administrators. The tenant is the route's <c>{tenantId}</c>, bound by
    /// <see cref="BindRouteTenantAttribute"/> only after the policy of the action has authorized the caller.
    /// Inventory data stays invisible (D8) and there is no role-definition endpoint (A2): roles are managed by
    /// the tenant's own administrators in <see cref="AdminController"/>.
    /// </summary>
    [Route("api/platform/tenants/{tenantId:int}")]
    [ApiController]
    [BindRouteTenant]
    public class PlatformTenantController : TenantManagementController
    {
        private readonly EntraOptions _entra;
        private readonly TimeProvider _time;

        public PlatformTenantController(
            InventoryDbContext context, TimeProvider time, IOptions<AuthOptions> auth, IOptions<EntraOptions> entra)
            : base(context, time, auth)
        {
            _entra = entra.Value;
            _time = time;
        }

        [HttpGet]
        [Authorize(Policy = AdminPolicies.PlatformTenantsRead)]
        public async Task<ActionResult<TenantDto>> GetTenant()
        {
            var tenant = await Context.Tenants.SingleAsync(t => t.Id == Context.RequireTenantId());

            return Ok(TenantDto.FromEntity(tenant));
        }

        [HttpPut("require-pin")]
        [Authorize(Policy = AdminPolicies.PlatformTenantsManage)]
        public Task<ActionResult<TenantDto>> SetRequirePin(SetRequirePinRequest request) =>
            SetRequirePinCore(request.Required!.Value);

        // ---- Devices ---------------------------------------------------------------------

        [HttpGet("devices")]
        [Authorize(Policy = AdminPolicies.PlatformDevicesManage)]
        public Task<ActionResult<IReadOnlyList<DeviceDto>>> ListDevices() => ListDevicesCore();

        [HttpPost("devices")]
        [Authorize(Policy = AdminPolicies.PlatformDevicesManage)]
        public Task<ActionResult<DeviceEnrollmentDto>> CreateDevice(CreateDeviceRequest request) =>
            CreateDeviceCore(request);

        [HttpPost("devices/{deviceId:int}/enrollment-code")]
        [Authorize(Policy = AdminPolicies.PlatformDevicesManage)]
        public Task<ActionResult<DeviceEnrollmentDto>> IssueEnrollmentCode(int deviceId) =>
            IssueEnrollmentCodeCore(deviceId);

        [HttpPost("devices/{deviceId:int}/activate")]
        [Authorize(Policy = AdminPolicies.PlatformDevicesManage)]
        public Task<ActionResult<DeviceDto>> ActivateDevice(int deviceId) => SetDeviceActiveCore(deviceId, true);

        [HttpPost("devices/{deviceId:int}/deactivate")]
        [Authorize(Policy = AdminPolicies.PlatformDevicesManage)]
        public Task<ActionResult<DeviceDto>> DeactivateDevice(int deviceId) => SetDeviceActiveCore(deviceId, false);

        // ---- Employees -------------------------------------------------------------------

        [HttpGet("employees")]
        [Authorize(Policy = AdminPolicies.PlatformEmployeesManage)]
        public Task<ActionResult<IReadOnlyList<EmployeeDto>>> ListEmployees() => ListEmployeesCore();

        [HttpPost("employees")]
        [Authorize(Policy = AdminPolicies.PlatformEmployeesManage)]
        public Task<ActionResult<EmployeeDto>> CreateEmployee(CreateEmployeeRequest request) =>
            CreateEmployeeCore(request);

        [HttpPut("employees/{employeeId:int}")]
        [Authorize(Policy = AdminPolicies.PlatformEmployeesManage)]
        public Task<ActionResult<EmployeeDto>> RenameEmployee(int employeeId, NameRequest request) =>
            RenameEmployeeCore(employeeId, request);

        [HttpPut("employees/{employeeId:int}/roles")]
        [Authorize(Policy = AdminPolicies.PlatformEmployeesManage)]
        public Task<ActionResult<EmployeeDto>> SetEmployeeRoles(int employeeId, SetEmployeeRolesRequest request) =>
            SetEmployeeRolesCore(employeeId, request);

        [HttpPut("employees/{employeeId:int}/pin")]
        [Authorize(Policy = AdminPolicies.PlatformEmployeesManage)]
        public Task<IActionResult> SetEmployeePin(int employeeId, SetEmployeePinRequest request) =>
            SetEmployeePinCore(employeeId, request);

        [HttpPost("employees/{employeeId:int}/activate")]
        [Authorize(Policy = AdminPolicies.PlatformEmployeesManage)]
        public Task<ActionResult<EmployeeDto>> ActivateEmployee(int employeeId) => SetEmployeeActiveCore(employeeId, true);

        [HttpPost("employees/{employeeId:int}/deactivate")]
        [Authorize(Policy = AdminPolicies.PlatformEmployeesManage)]
        public Task<ActionResult<EmployeeDto>> DeactivateEmployee(int employeeId) => SetEmployeeActiveCore(employeeId, false);

        // ---- Sessions --------------------------------------------------------------------

        [HttpGet("sessions")]
        [Authorize(Policy = AdminPolicies.PlatformSessionsRevoke)]
        public Task<ActionResult<IReadOnlyList<SessionDto>>> ListSessions([FromQuery] bool includeInactive = false) =>
            ListSessionsCore(includeInactive);

        [HttpPost("sessions/{sessionId:guid}/revoke")]
        [Authorize(Policy = AdminPolicies.PlatformSessionsRevoke)]
        public Task<IActionResult> RevokeSession(Guid sessionId) => RevokeSessionCore(sessionId);

        // ---- Tenant administrators -------------------------------------------------------
        //
        // How a Tenant Admin comes to exist: a Platform Admin maps an Entra identity (the object id of a user
        // or invited guest in the configured directory) to this tenant's TenantAdmin role. Until that row is
        // there, the person can sign in to Entra but is no administrator here.

        [HttpGet("administrators")]
        [Authorize(Policy = AdminPolicies.PlatformTenantsManage)]
        public async Task<ActionResult<IReadOnlyList<TenantAdminDto>>> ListAdministrators()
        {
            var admins = await Context.TenantAdmins.Include(a => a.Role).OrderBy(a => a.CreatedAt).ThenBy(a => a.Id).ToListAsync();

            return Ok(admins.Select(TenantAdminDto.FromEntity).ToList());
        }

        [HttpPost("administrators")]
        [Authorize(Policy = AdminPolicies.PlatformTenantsManage)]
        public async Task<ActionResult<TenantAdminDto>> AddAdministrator(CreateTenantAdminRequest request)
        {
            if (!_entra.Enabled)
                return Clash("Entra is not configured, so no administrator could sign in.");

            var role = await Context.Roles.Include(r => r.Permissions)
                .FirstOrDefaultAsync(r => r.Name == DefaultRoles.TenantAdmin && r.Scope == PermissionScope.Admin);

            if (role is null)
                return Clash("The tenant has no TenantAdmin role.");

            TenantAdmin admin;

            try
            {
                admin = new TenantAdmin(role, _entra.GetDirectoryId(), request.ObjectId!.Value, _time.GetUtcNow(), request.DisplayName);
            }
            catch (ArgumentException ex)
            {
                return BadInput(ex.Message);
            }

            // An identity administers one tenant, so look across all of them. The answer doesn't say which.
            var taken = await Context.TenantAdmins.IgnoreQueryFilters()
                .AnyAsync(a => a.EntraTenantId == admin.EntraTenantId && a.EntraObjectId == admin.EntraObjectId);

            if (taken)
                return Clash("This identity is already an administrator (of this or another tenant).");

            Context.TenantAdmins.Add(admin);

            try
            {
                await Context.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                return Clash("This identity is already an administrator (of this or another tenant).");
            }

            return Ok(TenantAdminDto.FromEntity(admin));
        }

        [HttpPost("administrators/{adminId:int}/activate")]
        [Authorize(Policy = AdminPolicies.PlatformTenantsManage)]
        public Task<ActionResult<TenantAdminDto>> ActivateAdministrator(int adminId) => SetAdministratorActive(adminId, true);

        [HttpPost("administrators/{adminId:int}/deactivate")]
        [Authorize(Policy = AdminPolicies.PlatformTenantsManage)]
        public Task<ActionResult<TenantAdminDto>> DeactivateAdministrator(int adminId) => SetAdministratorActive(adminId, false);

        private async Task<ActionResult<TenantAdminDto>> SetAdministratorActive(int adminId, bool active)
        {
            var admin = await Context.TenantAdmins.Include(a => a.Role).FirstOrDefaultAsync(a => a.Id == adminId);

            if (admin is null)
                return Missing("Administrator");

            if (active)
                admin.Activate();
            else
                admin.Deactivate(_time.GetUtcNow());

            await Context.SaveChangesAsync();

            return Ok(TenantAdminDto.FromEntity(admin));
        }
    }
}
