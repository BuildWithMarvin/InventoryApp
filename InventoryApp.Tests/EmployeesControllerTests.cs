using InventoryApp.Api.Controllers;
using InventoryApp.Api.Models;
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
    public async Task Login_ReturnsOk_WhenBarcodeIsValid()
    {
        var controller = new EmployeesController(Context);

        var employee = new Employee
        {
            Name = "Bob",
            BadgeBarcode = "EMP-12345",
            Role = "User"
        };

        Context.Employees.Add(employee);
        await Context.SaveChangesAsync();

        var loginRequest = new LoginRequest
        {
            BadgeBarcode = "EMP-12345"
        };

        var result =
            await controller.Login(loginRequest);

        var okResult =
            Assert.IsType<OkObjectResult>(result);

        Assert.NotNull(okResult.Value);
    }

    [Fact]
    public async Task Login_ReturnsUnauthorized_WhenBarcodeIsInvalid()
    {
        var controller = new EmployeesController(Context);

        var loginRequest = new LoginRequest
        {
            BadgeBarcode = "WRONG-BARCODE"
        };

        var result =
            await controller.Login(loginRequest);

        Assert.IsType<UnauthorizedObjectResult>(result);
    }

    [Fact]
    public async Task CreateEmployee_SavesToDatabase_AndReturnsOk()
    {
        var controller = new EmployeesController(Context);

        var newEmployee = new Employee
        {
            Name = "New admin",
            BadgeBarcode = "ADMIN-999",
            Role = "Admin"
        };

        var result =
            await controller.CreateEmployee(newEmployee);

        var okResult =
            Assert.IsType<OkObjectResult>(result);

        var returnedEmployee =
            Assert.IsType<Employee>(okResult.Value);

        Assert.Equal(
            "ADMIN-999",
            returnedEmployee.BadgeBarcode);

        Assert.Equal(
            "Admin",
            returnedEmployee.Role);

        var employeeInDb =
            await Context.Employees
                .FirstOrDefaultAsync(
                    e => e.BadgeBarcode == "ADMIN-999");

        Assert.NotNull(employeeInDb);
    }
}
