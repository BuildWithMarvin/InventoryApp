using System.Security.Claims;
using InventoryApp.Api.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

public static class ControllerTestExtensions
{
    public static TController AsEmployee<TController>(this TController controller, int employeeId)
        where TController : ControllerBase
    {
        var identity = new ClaimsIdentity(
            [new Claim(InventoryClaims.Subject, employeeId.ToString())],
            authenticationType: "Test");

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) }
        };

        return controller;
    }
}
