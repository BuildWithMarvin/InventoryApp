using System.Globalization;
using InventoryApp.Api.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;

namespace InventoryApp.Api.Security
{
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class BindRouteTenantAttribute : Attribute, IAsyncActionFilter
    {
        public const string RouteKey = "tenantId";

        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            var services = context.HttpContext.RequestServices;
            var tenantContext = services.GetRequiredService<TenantContext>();

            if (tenantContext.InventoryAccess)
            {
                // Not an administrator request. Never let such a request pick its tenant.
                context.Result = new ForbidResult();
                return;
            }

            if (!context.RouteData.Values.TryGetValue(RouteKey, out var raw)
                || !int.TryParse(Convert.ToString(raw, CultureInfo.InvariantCulture), NumberStyles.None, CultureInfo.InvariantCulture, out var tenantId)
                || tenantId <= 0)
            {
                context.Result = new NotFoundResult();
                return;
            }

            var db = services.GetRequiredService<InventoryDbContext>();

            if (!await db.Tenants.AnyAsync(t => t.Id == tenantId, context.HttpContext.RequestAborted))
            {
                context.Result = Problem(StatusCodes.Status404NotFound, "Tenant not found.");
                return;
            }

            if (tenantContext.TenantId is { } bound && bound != tenantId)
            {
                context.Result = Problem(StatusCodes.Status409Conflict,
                    "This identity is also a Tenant Admin of another tenant. Use an account that is only a Platform Admin.");
                return;
            }

            if (tenantContext.TenantId is null)
                tenantContext.Set(tenantId);

            await next();
        }

        private static ObjectResult Problem(int status, string title) =>
            new(new ProblemDetails { Status = status, Title = title }) { StatusCode = status };
    }
}
