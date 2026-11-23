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
    [Route("api/admin")]
    [ApiController]
    public class AdminController : TenantManagementController
    {
        public AdminController(InventoryDbContext context, TimeProvider time, IOptions<AuthOptions> auth)
            : base(context, time, auth)
        {
        }

        [HttpGet("me")]
        [Authorize(Policy = AdminPolicies.Session)]
        public async Task<ActionResult<AdminProfileDto>> GetProfile()
        {
            var kinds = User.FindAll(InventoryClaims.AdminKind).Select(c => c.Value).Order().ToList();

            TenantSummaryDto? tenant = null;

            if (kinds.Contains(InventoryClaims.AdminKindTenant))
            {
                var row = await Context.Tenants.SingleAsync(t => t.Id == Context.RequireTenantId());
                tenant = TenantSummaryDto.FromEntity(row);
            }

            return Ok(new AdminProfileDto(
                kinds,
                tenant,
                Values(InventoryClaims.Permission),
                Values(InventoryClaims.PlatformPermission)));
        }

        [HttpGet("tenant")]
        [Authorize(Policy = AdminPolicies.TenantSettings)]
        public async Task<ActionResult<TenantDto>> GetTenant()
        {
            var tenant = await Context.Tenants.SingleAsync(t => t.Id == Context.RequireTenantId());

            return Ok(TenantDto.FromEntity(tenant));
        }

        [HttpPut("tenant/require-pin")]
        [Authorize(Policy = AdminPolicies.TenantSettings)]
        public Task<ActionResult<TenantDto>> SetRequirePin(SetRequirePinRequest request) =>
            SetRequirePinCore(request.Required!.Value);

        [HttpGet("devices")]
        [Authorize(Policy = AdminPolicies.DevicesManage)]
        public Task<ActionResult<IReadOnlyList<DeviceDto>>> ListDevices() => ListDevicesCore();

        [HttpPost("devices")]
        [Authorize(Policy = AdminPolicies.DevicesManage)]
        public Task<ActionResult<DeviceEnrollmentDto>> CreateDevice(CreateDeviceRequest request) =>
            CreateDeviceCore(request);

        [HttpPost("devices/{deviceId:int}/enrollment-code")]
        [Authorize(Policy = AdminPolicies.DevicesManage)]
        public Task<ActionResult<DeviceEnrollmentDto>> IssueEnrollmentCode(int deviceId) =>
            IssueEnrollmentCodeCore(deviceId);

        [HttpPost("devices/{deviceId:int}/activate")]
        [Authorize(Policy = AdminPolicies.DevicesManage)]
        public Task<ActionResult<DeviceDto>> ActivateDevice(int deviceId) => SetDeviceActiveCore(deviceId, true);

        [HttpPost("devices/{deviceId:int}/deactivate")]
        [Authorize(Policy = AdminPolicies.DevicesManage)]
        public Task<ActionResult<DeviceDto>> DeactivateDevice(int deviceId) => SetDeviceActiveCore(deviceId, false);

        [HttpGet("employees")]
        [Authorize(Policy = AdminPolicies.EmployeesManage)]
        public Task<ActionResult<IReadOnlyList<EmployeeDto>>> ListEmployees() => ListEmployeesCore();

        [HttpPost("employees")]
        [Authorize(Policy = AdminPolicies.EmployeesManage)]
        public Task<ActionResult<EmployeeDto>> CreateEmployee(CreateEmployeeRequest request) =>
            CreateEmployeeCore(request);

        [HttpPut("employees/{employeeId:int}")]
        [Authorize(Policy = AdminPolicies.EmployeesManage)]
        public Task<ActionResult<EmployeeDto>> RenameEmployee(int employeeId, NameRequest request) =>
            RenameEmployeeCore(employeeId, request);

        [HttpPut("employees/{employeeId:int}/roles")]
        [Authorize(Policy = AdminPolicies.EmployeesManage)]
        public Task<ActionResult<EmployeeDto>> SetEmployeeRoles(int employeeId, SetEmployeeRolesRequest request) =>
            SetEmployeeRolesCore(employeeId, request);

        [HttpPut("employees/{employeeId:int}/pin")]
        [Authorize(Policy = AdminPolicies.EmployeesManage)]
        public Task<IActionResult> SetEmployeePin(int employeeId, SetEmployeePinRequest request) =>
            SetEmployeePinCore(employeeId, request);

        [HttpPost("employees/{employeeId:int}/activate")]
        [Authorize(Policy = AdminPolicies.EmployeesManage)]
        public Task<ActionResult<EmployeeDto>> ActivateEmployee(int employeeId) => SetEmployeeActiveCore(employeeId, true);

        [HttpPost("employees/{employeeId:int}/deactivate")]
        [Authorize(Policy = AdminPolicies.EmployeesManage)]
        public Task<ActionResult<EmployeeDto>> DeactivateEmployee(int employeeId) => SetEmployeeActiveCore(employeeId, false);

        [HttpGet("sessions")]
        [Authorize(Policy = AdminPolicies.SessionsRevoke)]
        public Task<ActionResult<IReadOnlyList<SessionDto>>> ListSessions([FromQuery] bool includeInactive = false) =>
            ListSessionsCore(includeInactive);

        [HttpPost("sessions/{sessionId:guid}/revoke")]
        [Authorize(Policy = AdminPolicies.SessionsRevoke)]
        public Task<IActionResult> RevokeSession(Guid sessionId) => RevokeSessionCore(sessionId);

        // Only Inventory roles can be created and changed here. Admin roles (TenantAdmin and any other) are
        // listed but read-only: otherwise an administrator holding Roles.Manage could grant themselves more
        // admin permissions than they were given.

        [HttpGet("permissions")]
        [Authorize(Policy = AdminPolicies.RolesManage)]
        public ActionResult<PermissionCatalogDto> GetPermissionCatalog() =>
            Ok(new PermissionCatalogDto(
                Permissions.Of(PermissionScope.Inventory).Order().ToList(),
                Permissions.Of(PermissionScope.Admin).Order().ToList()));

        [HttpGet("roles")]
        [Authorize(Policy = AdminPolicies.RolesManage)]
        public async Task<ActionResult<IReadOnlyList<RoleDto>>> ListRoles()
        {
            var roles = await Context.Roles.Include(r => r.Permissions).OrderBy(r => r.Name).ToListAsync();

            return Ok(roles.Select(RoleDto.FromEntity).ToList());
        }

        [HttpPost("roles")]
        [Authorize(Policy = AdminPolicies.RolesManage)]
        public async Task<ActionResult<RoleDto>> CreateRole(RoleRequest request)
        {
            Role role;

            try
            {
                role = new Role(Context.RequireTenantId(), request.Name, PermissionScope.Inventory, request.Permissions);
            }
            catch (ArgumentException ex)
            {
                return BadInput(ex.Message);
            }

            if (await Context.Roles.AnyAsync(r => r.Name == role.Name))
                return Clash("A role with this name already exists.");

            Context.Roles.Add(role);

            try
            {
                await Context.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                return Clash("A role with this name already exists.");
            }

            return Ok(RoleDto.FromEntity(role));
        }

        [HttpPut("roles/{roleId:int}")]
        [Authorize(Policy = AdminPolicies.RolesManage)]
        public async Task<ActionResult<RoleDto>> UpdateRole(int roleId, RoleRequest request)
        {
            var role = await Context.Roles.Include(r => r.Permissions).FirstOrDefaultAsync(r => r.Id == roleId);

            if (role is null)
                return Missing("Role");

            if (NotManageable(role) is { } refusal)
                return refusal;

            try
            {
                role.Rename(request.Name);
                role.SetPermissions(request.Permissions);
            }
            catch (ArgumentException ex)
            {
                return BadInput(ex.Message);
            }

            if (await Context.Roles.AnyAsync(r => r.Name == role.Name && r.Id != role.Id))
                return Clash("A role with this name already exists.");

            try
            {
                await Context.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                return Clash("A role with this name already exists, or the role was changed at the same moment.");
            }

            return Ok(RoleDto.FromEntity(role));
        }

        [HttpDelete("roles/{roleId:int}")]
        [Authorize(Policy = AdminPolicies.RolesManage)]
        public async Task<IActionResult> DeleteRole(int roleId)
        {
            var role = await Context.Roles.FirstOrDefaultAsync(r => r.Id == roleId);

            if (role is null)
                return Missing("Role");

            if (NotManageable(role) is { } refusal)
                return refusal;

            if (await Context.EmployeeRoles.AnyAsync(er => er.RoleId == roleId))
                return Clash("The role is still assigned to employees.");

            Context.Roles.Remove(role);

            try
            {
                await Context.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                return Clash("The role is still in use.");
            }

            return NoContent();
        }

        private ObjectResult? NotManageable(Role role)
        {
            if (role.IsSystemRole)
                return Clash($"The system role '{role.Name}' cannot be changed.");

            if (role.Scope != PermissionScope.Inventory)
                return Clash("Admin roles cannot be changed through the API.");

            return null;
        }

        private List<string> Values(string claimType) =>
            User.FindAll(claimType).Select(c => c.Value).Order().ToList();
    }
}
