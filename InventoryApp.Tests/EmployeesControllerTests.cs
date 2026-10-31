using InventoryApp.Api.Controllers;
using InventoryApp.Api.Dtos;
using InventoryApp.Api.Models;
using InventoryApp.Api.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

[Trait("Category", "Integration")]
[Collection("Integration Tests")]
public class EmployeesControllerTests : IntegrationTestBase
{
    public EmployeesControllerTests(DatabaseFixture databaseFixture)
        : base(databaseFixture)
    {
    }

    [Fact]
    public async Task CreateEmployee_SavesToDatabase_AndReturnsOk()
    {
        var controller = new EmployeesController(Context);

        var newEmployee = new CreateEmployeeRequest(
            "New manager",
            "MANAGER-999",
            DefaultRoles.WarehouseManager);

        var result =
            await controller.CreateEmployee(newEmployee);

        var okResult =
            Assert.IsType<OkObjectResult>(result.Result);

        var returnedEmployee =
            Assert.IsType<EmployeeDto>(okResult.Value);

        Assert.Equal("New manager", returnedEmployee.Name);

        Assert.Equal(
            new[] { DefaultRoles.WarehouseManager },
            returnedEmployee.Roles);

        var employeeInDb =
            await Context.Employees
                .FirstOrDefaultAsync(
                    e => e.BadgeBarcode == "MANAGER-999");

        Assert.NotNull(employeeInDb);
    }

    [Fact]
    public async Task CreateEmployee_AssignsWarehouseEmployee_WhenNoRoleIsGiven()
    {
        var controller = new EmployeesController(Context);

        var result = await controller.CreateEmployee(new CreateEmployeeRequest("Tom", "EMP-1"));

        var returned = Assert.IsType<EmployeeDto>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal(new[] { DefaultRoles.WarehouseEmployee }, returned.Roles);
    }

    [Fact]
    public async Task CreateEmployee_ReturnsBadRequest_WhenRoleDoesNotExist()
    {
        var controller = new EmployeesController(Context);

        var result = await controller.CreateEmployee(new CreateEmployeeRequest("Tom", "EMP-1", "NoSuchRole"));

        Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.False(await Context.Employees.AnyAsync());
    }

    [Fact]
    public async Task CreateEmployee_ReturnsBadRequest_ForAnAdminRole()
    {
        var controller = new EmployeesController(Context);

        var result = await controller.CreateEmployee(new CreateEmployeeRequest("Tom", "EMP-1", DefaultRoles.TenantAdmin));

        Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.False(await Context.Employees.AnyAsync());
    }
}
