using System.Reflection;
using InventoryApp.Api.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

namespace InventoryApp.Tests.Unit;

/// <summary>
/// Fails when someone adds an endpoint without deciding who may call it. Every action must carry an
/// explicit [Authorize(Policy = ...)] or [AllowAnonymous], and the list of anonymous endpoints is fixed.
/// </summary>
[Trait("Category", "Unit")]
public class EndpointGuardrailTests
{
    // Anyone can call these: they are how a device and an employee get credentials in the first place.
    private static readonly string[] ExpectedAnonymous = ["AuthController.Enroll", "AuthController.Login"];

    private static readonly string[] KnownPolicies =
    [
        ..Permissions.Of(PermissionScope.Inventory),
        Policies.EmployeeSession,
        Policies.AdminOnly,
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
            var onAdminRoute = AdminRoutePrefixes.Contains(RouteOf(action.Controller));

            Assert.False(usesAdmin && usesEmployee, $"{Name(action)} mixes administrator and employee policies.");
            Assert.False(usesAdmin && !onAdminRoute, $"{Name(action)} uses an administrator policy outside /api/admin and /api/platform.");
            Assert.False(onAdminRoute && !usesAdmin, $"{Name(action)} is on an administrator route without an administrator policy.");
        }
    }

    [Fact]
    public void EmployeeEndpoints_DoNotLiveOnAdministratorRoutes()
    {
        // The reverse of the rule above, stated on controllers: the employee-facing controllers keep their own routes.
        var employeeControllers = new[] { typeof(Api.Controllers.ProductsController), typeof(Api.Controllers.AuthController), typeof(Api.Controllers.EmployeesController) };

        Assert.All(employeeControllers, c => Assert.DoesNotContain(RouteOf(c), AdminRoutePrefixes));
    }
}
