using System.Security.Claims;
using InventoryApp.Api.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

public static class ControllerTestExtensions
{
    /// <summary>
    /// Calls to a controller made directly (without HTTP) have no authenticated user. This gives it
    /// the one the validated session would provide.
    /// </summary>
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
