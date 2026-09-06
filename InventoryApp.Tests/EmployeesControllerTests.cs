using InventoryApp.Api.Controllers;
using InventoryApp.Api.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

[Trait("Category", "Integration")]
[Collection("Integration Tests")]
public class EmployeesControllerTests
{
    private readonly DatabaseFixture _databaseFixture;

    public EmployeesControllerTests(DatabaseFixture databaseFixture)
    {
        _databaseFixture = databaseFixture;
    }

    [Fact]
    public async Task Login_ReturnsOk_WhenBarcodeIsValid()
    {   
        await using var context =
            _databaseFixture.CreateContext();

        var controller = new EmployeesController(context);

        var employee = new Employee
        {
            Id = 1,
            Name = "Bob",
            BadgeBarcode = "EMP-12345",
            Role = "User"
        };

        context.Employees.Add(employee);
        await context.SaveChangesAsync();

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
        await using var context =
            _databaseFixture.CreateContext();

        var controller = new EmployeesController(context);

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
        await using var context =
            _databaseFixture.CreateContext();

        var controller = new EmployeesController(context);

        var newEmployee = new Employee
        {
            Id = 2,
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
            await context.Employees
                .FirstOrDefaultAsync(
                    e => e.BadgeBarcode == "ADMIN-999");


        Assert.NotNull(employeeInDb);
    }
}






