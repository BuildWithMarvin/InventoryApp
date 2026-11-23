using InventoryApp.Api.Security;

namespace InventoryApp.Api.Models
{
    public class EmployeeRole : ITenantOwned
    {
        private EmployeeRole() { }

        internal EmployeeRole(Employee employee, Role role)
        {
            Employee = employee;
            Role = role;
            TenantId = employee.TenantId;
            RoleScope = role.Scope;
        }

        public int TenantId { get; private set; }

        public int EmployeeId { get; private set; }

        public int RoleId { get; private set; }

        public PermissionScope RoleScope { get; private set; }

        public Employee? Employee { get; private set; }

        public Role? Role { get; private set; }
    }
}
