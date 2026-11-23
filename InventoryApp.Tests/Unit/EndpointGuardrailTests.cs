using System.Reflection;
using InventoryApp.Api.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

namespace InventoryApp.Tests.Unit;

[Trait("Category", "Unit")]
public class EndpointGuardrailTests
{
    // Anyone can call these: they are how a device and an employee get credentials in the first place.
    private static readonly string[] ExpectedAnonymous = ["AuthController.Enroll", "AuthController.Login"];

    private static readonly string[] KnownPolicies =
    [
        ..Permissions.Of(PermissionScope.Inventory),
        Policies.EmployeeSession,
        ..AdminPolicies.All,
    ];

    private static IEnumerable<(Type Controller, MethodInfo Action)> Actions() =>
        typeof(Program).Assembly.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract)
            .SelectMany(controller => controller
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(m => m.GetCustomAttributes<HttpMethodAttribute>().Any())
                .Select(action => (controller, action)));

    private static string Name((Type Controller, MethodInfo Action) a) => $"{a.Controller.Name}.{a.Action.Name}";

    private static IEnumerable<AuthorizeAttribute> AuthorizeAttributes((Type Controller, MethodInfo Action) a) =>
        a.Action.GetCustomAttributes<AuthorizeAttribute>().Concat(a.Controller.GetCustomAttributes<AuthorizeAttribute>());

    private static bool IsAnonymous((Type Controller, MethodInfo Action) a) =>
        a.Action.GetCustomAttributes<AllowAnonymousAttribute>().Any()
        || a.Controller.GetCustomAttributes<AllowAnonymousAttribute>().Any();

    [Fact]
    public void ThereAreEndpoints_ToCheck()
    {
        Assert.NotEmpty(Actions());
    }

    [Fact]
    public void EveryAction_IsExplicitlyAuthorizedOrAnonymous()
    {
        var undecided = Actions().Where(a => !IsAnonymous(a) && !AuthorizeAttributes(a).Any()).Select(Name);

        Assert.Empty(undecided);
    }

    [Fact]
    public void OnlyTheExpectedActions_AreAnonymous()
    {
        var anonymous = Actions().Where(IsAnonymous).Select(Name).Order();

        Assert.Equal(ExpectedAnonymous.Order(), anonymous);
    }

    [Fact]
    public void EveryAuthorizedAction_NamesAKnownPolicy()
    {
        // A bare [Authorize] would use the default policy, which has no authentication scheme here.
        var offenders = Actions()
            .Where(a => !IsAnonymous(a))
            .Where(a => AuthorizeAttributes(a).Any(auth => auth.Policy is null || !KnownPolicies.Contains(auth.Policy)))
            .Select(Name);

        Assert.Empty(offenders);
    }

    [Fact]
    public void NoPolicy_GrantsEmployeesAnAdminPermission()
    {
        Assert.Empty(KnownPolicies.Where(p => Permissions.IsDefined(p) && Permissions.ScopeOf(p) == PermissionScope.Admin));
    }

    [Fact]
    public void NoAdminPermissionName_IsAnEmployeePolicy_AndEveryAdminPolicyHasAPrefix()
    {
        // Admin policies are prefixed ("TenantAdmin:Tenant.Settings"), so a policy named after a bare permission can
        // only ever be an employee (inventory) policy.
        Assert.All(AdminPolicies.All.Where(p => p != AdminPolicies.Session), p =>
            Assert.True(p.StartsWith(AdminPolicies.TenantAdminPrefix) || p.StartsWith(AdminPolicies.PlatformAdminPrefix), p));
    }

    private static readonly string[] AdminRoutePrefixes = ["api/admin", "api/platform"];

    private static bool IsAdminRoute(string? route) =>
        route is not null && AdminRoutePrefixes.Any(prefix => route == prefix || route.StartsWith(prefix + "/"));

    private static string? RouteOf(Type controller) =>
        controller.GetCustomAttributes<RouteAttribute>().Select(r => r.Template).FirstOrDefault();

    [Fact]
    public void AdministratorPolicies_AreOnlyUsedOnAdministratorRoutes_AndNeverMixedWithEmployeePolicies()
    {
        foreach (var action in Actions())
        {
            var policies = AuthorizeAttributes(action).Select(a => a.Policy).OfType<string>().ToList();
            var usesAdmin = policies.Any(AdminPolicies.All.Contains);
            var usesEmployee = policies.Any(p => !AdminPolicies.All.Contains(p));
            var onAdminRoute = IsAdminRoute(RouteOf(action.Controller));

            Assert.False(usesAdmin && usesEmployee, $"{Name(action)} mixes administrator and employee policies.");
            Assert.False(usesAdmin && !onAdminRoute, $"{Name(action)} uses an administrator policy outside /api/admin and /api/platform.");
            Assert.False(onAdminRoute && !usesAdmin, $"{Name(action)} is on an administrator route without an administrator policy.");
        }
    }

    [Fact]
    public void EmployeeEndpoints_DoNotLiveOnAdministratorRoutes()
    {
        // The reverse of the rule above, stated on controllers: the employee-facing controllers keep their own routes.
        var employeeControllers = new[] { typeof(Api.Controllers.ProductsController), typeof(Api.Controllers.AuthController) };

        Assert.All(employeeControllers, c => Assert.False(IsAdminRoute(RouteOf(c)), c.Name));
    }

    [Fact]
    public void EveryPlatformController_WithATenantInItsRoute_BindsItOnlyThroughTheFilter()
    {
        // The route's tenant id is only a selector. The filter binds it after authorization; nothing else may.
        var controllers = typeof(Program).Assembly.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract)
            .Where(t => RouteOf(t) is { } route && route.StartsWith("api/platform") && route.Contains("{tenantId"))
            .ToList();

        Assert.NotEmpty(controllers);
        Assert.All(controllers, c => Assert.NotEmpty(c.GetCustomAttributes<BindRouteTenantAttribute>()));
    }

    [Fact]
    public void NoAction_TakesATenantIdParameter_ExceptThroughTheRoute()
    {
        // Requests never carry a TenantId. Route parameters are bound by the filter, so actions don't take them.
        var offenders = Actions()
            .Where(a => a.Action.GetParameters().Any(p => p.Name!.Contains("tenantId", StringComparison.OrdinalIgnoreCase)))
            .Select(Name);

        Assert.Empty(offenders);
    }
}
