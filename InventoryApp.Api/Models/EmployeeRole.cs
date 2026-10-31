using InventoryApp.Api.Security;

namespace InventoryApp.Api.Models
{
    /// <summary>
    /// Gives an employee a role. Created through <see cref="Employee.AssignRole"/>.
    /// RoleScope repeats the role's scope so that a composite foreign key can make the
    /// database itself refuse any role that isn't an Inventory role.
    /// </summary>
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
