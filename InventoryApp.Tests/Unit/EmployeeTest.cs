using InventoryApp.Api.Models;

namespace InventoryApp.Tests.Unit;

[Trait("Category", "Unit")]
public class EmployeeTest
{   [Fact]
    public void Constructor_CreatesEmployee_WithValidData()
    {

        var employee = new Employee(
            "Tom",
            "EMP-12345"
        );


        Assert.Equal("Tom", employee.Name);
        Assert.Equal("EMP-12345", employee.BadgeBarcode);
        Assert.Equal("User", employee.Role);
    }
    [Fact]
    public void Constructor_TrimsName()
    {

        var employee = new Employee(
                  " Tom ",
                  "EMP-12345"
              );

        Assert.Equal("Tom", employee.Name);
    }
    [Fact]
     public void Constructor_TrimsBadgeBarcode()
    {

        var employee = new Employee(
                  "Tom",
                  " EMP-12345 "
              );

        Assert.Equal("EMP-12345", employee.BadgeBarcode);
    }
    [Fact]
    public void Constructor_TrimsRole()
    {

        var employee = new Employee(
                  "Tom",
                  "EMP-12345",
                  " Admin "
              );

        Assert.Equal("Admin", employee.Role);
    }
    [Fact]
       public void Constructor_ThrowsArgumentException_WhenNameIsEmpty()
        {

            Assert.Throws<ArgumentException>(() =>
                new Employee("", "EMP-12345"));
        }
    [Fact]
        public void Constructor_ThrowsArgumentException_WhenNameIsWhitespace()
        {

            Assert.Throws<ArgumentException>(() =>
                new Employee("   ", "EMP-12345"));
        }
    [Fact]
        public void Constructor_ThrowsArgumentException_WhenBadgeBarcodeIsEmpty()
        {

            Assert.Throws<ArgumentException>(() =>
                new Employee("Tom", ""));
        }
    [Fact]
        public void Constructor_ThrowsArgumentException_WhenBadgeBarcodeIsWhitespace()
        {

            Assert.Throws<ArgumentException>(() =>
                new Employee("Tom", "   "));
        }
    [Fact]
        public void Constructor_ThrowsArgumentException_RoleIsNull()
        {

            Assert.Throws<ArgumentException>(() =>
                new Employee("Tom", "EMP-12345", null));
        }
    [Fact]
         public void Constructor_ThrowsArgumentException_RoleIsWhitespace()
        {

            Assert.Throws<ArgumentException>(() =>
                new Employee("Tom", "EMP-12345", "   "));
        }
}
