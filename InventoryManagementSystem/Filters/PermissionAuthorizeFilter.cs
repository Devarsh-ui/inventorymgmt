using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.DependencyInjection;
using InventoryManagementSystem.Interfaces;
using InventoryManagementSystem.Models;
using System;
using System.Security.Claims;
using System.Threading.Tasks;

namespace InventoryManagementSystem.Filters
{
    public class PermissionAuthorizeFilter : IAsyncActionFilter
    {
        private readonly IPermissionService _permissionService;
        private readonly IPlatformSettingsService _platformSettingsService;

        public PermissionAuthorizeFilter(
            IPermissionService permissionService,
            IPlatformSettingsService platformSettingsService)
        {
            _permissionService = permissionService;
            _platformSettingsService = platformSettingsService;
        }

        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            var controllerName = context.RouteData.Values["controller"]?.ToString() ?? string.Empty;
            var actionName = context.RouteData.Values["action"]?.ToString() ?? string.Empty;
            var user = context.HttpContext.User;
            var isSuperAdmin = user?.Identity?.IsAuthenticated == true && user.IsInRole(Role.SuperAdmin);

            // Maintenance Mode Enforcement (Bypassed for SuperAdmin so they can manage the platform)
            if (!isSuperAdmin)
            {
                // 1. Check Public Website Maintenance Mode
                if (controllerName.Equals("Landing", StringComparison.OrdinalIgnoreCase))
                {
                    if (!actionName.Equals("Maintenance", StringComparison.OrdinalIgnoreCase))
                    {
                        var (isMaint, msg, endTime) = await _platformSettingsService.CheckWebsiteMaintenanceAsync(actionName);
                        if (isMaint)
                        {
                            context.Result = new RedirectToActionResult("Maintenance", "Landing", null);
                            return;
                        }
                    }
                }
                // 2. Check Shop Dashboard Maintenance Mode
                else if (!controllerName.Equals("Account", StringComparison.OrdinalIgnoreCase) &&
                         !controllerName.Equals("PlatformAdmin", StringComparison.OrdinalIgnoreCase) &&
                         !controllerName.Equals("SystemLog", StringComparison.OrdinalIgnoreCase) &&
                         !controllerName.Equals("InventoryAlert", StringComparison.OrdinalIgnoreCase))
                {
                    if (!(controllerName.Equals("Home", StringComparison.OrdinalIgnoreCase) && actionName.Equals("Maintenance", StringComparison.OrdinalIgnoreCase)))
                    {
                        var (isMaint, msg, endTime) = await _platformSettingsService.CheckShopDashboardMaintenanceAsync();
                        if (isMaint)
                        {
                            context.Result = new RedirectToActionResult("Maintenance", "Home", null);
                            return;
                        }
                    }
                }
            }

            // Check if AllowAnonymous is present on action or controller
            var endpoint = context.HttpContext.GetEndpoint();
            if (endpoint?.Metadata?.GetMetadata<IAllowAnonymous>() != null)
            {
                await next();
                return;
            }

            // Allow AccountController and LandingController (Public SaaS website) for all users
            if (controllerName.Equals("Account", StringComparison.OrdinalIgnoreCase) ||
                controllerName.Equals("Landing", StringComparison.OrdinalIgnoreCase))
            {
                await next();
                return;
            }

            // 1. Unauthenticated -> Redirect to Login
            if (user?.Identity == null || !user.Identity.IsAuthenticated)
            {
                context.Result = new RedirectToActionResult("Login", "Account", null);
                return;
            }

            // Allow NotificationsController for all active authenticated users
            if (controllerName.Equals("Notifications", StringComparison.OrdinalIgnoreCase))
            {
                await next();
                return;
            }

            // Allow Suppliers full access to SupplierDashboard & Category management
            if (user.IsInRole(Role.Supplier))
            {
                if (controllerName.Equals("SupplierDashboard", StringComparison.OrdinalIgnoreCase) ||
                    controllerName.Equals("Category", StringComparison.OrdinalIgnoreCase))
                {
                    await next();
                    return;
                }

                // If supplier attempts to access non-supplier modules, block with 403
                context.HttpContext.Response.StatusCode = 403;
                context.Result = new ViewResult
                {
                    ViewName = "~/Views/Shared/AccessDenied.cshtml",
                    StatusCode = 403
                };
                return;
            }

            var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!string.IsNullOrEmpty(userId))
            {
                // Retrieve live user state (using fast memory cache with instant admin invalidation)
                var liveState = await _permissionService.GetLiveUserStateAsync(userId);

                // Forced Logout & Session Invalidation if locked or deleted
                if (!liveState.IsValid || liveState.IsLocked)
                {
                    await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

                    var factory = context.HttpContext.RequestServices.GetRequiredService<ITempDataDictionaryFactory>();
                    var tempData = factory.GetTempData(context.HttpContext);
                    tempData["ToastMessage"] = "Your account permissions or status have changed. Please contact administrator if you believe this is an error.";
                    tempData["ToastType"] = "warning";

                    context.Result = new RedirectToActionResult("Login", "Account", null);
                    return;
                }
            }

            // 2. SuperAdmin -> Full access everywhere
            if (user.IsInRole(Role.SuperAdmin))
            {
                await next();
                return;
            }

            // Server-Side SaaS Tenant Package Feature Enforcement
            var tenantId = user.FindFirst("TenantId")?.Value ?? user.FindFirst(ClaimTypes.GroupSid)?.Value;
            var featureCatalog = context.HttpContext.RequestServices.GetService<IFeatureCatalogService>();
            if (featureCatalog != null && !string.IsNullOrEmpty(tenantId))
            {
                var isFeatureEnabled = await featureCatalog.IsFeatureEnabledForTenantAsync(tenantId, controllerName, actionName);
                if (!isFeatureEnabled)
                {
                    var feature = featureCatalog.GetFeatureByControllerAndAction(controllerName, actionName);
                    var licenseService = context.HttpContext.RequestServices.GetService<ILicenseService>();
                    var pkg = licenseService != null ? await licenseService.GetTenantPackageAsync(tenantId) : null;

                    context.HttpContext.Response.StatusCode = 403;
                    var viewResult = new ViewResult
                    {
                        ViewName = "~/Views/Shared/FeatureNotIncluded.cshtml",
                        StatusCode = 403
                    };
                    viewResult.ViewData["FeatureName"] = feature?.FeatureName ?? controllerName;
                    viewResult.ViewData["CurrentPlanName"] = pkg?.Name ?? "Basic Plan";
                    context.Result = viewResult;
                    return;
                }
            }

            // Admin Role -> Allowed if tenant package includes the module
            if (user.IsInRole(Role.Admin))
            {
                await next();
                return;
            }

            // 3. Verify Live Employee Permission
            var hasAccess = await _permissionService.HasLivePermissionAsync(user, controllerName, actionName);

            if (!hasAccess)
            {
                var auditLogService = context.HttpContext.RequestServices.GetService<IAuditLogService>();
                if (auditLogService != null)
                {
                    var username = user.Identity?.Name ?? "Unknown";
                    await auditLogService.LogSecurityEventAsync(
                        "Unauthorized Access Attempt",
                        $"Employee @{username} attempted unauthorized action '{actionName}' on module '{controllerName}'",
                        "Failed",
                        "Warning");
                }

                // Short-circuit with 403 Access Denied view
                context.HttpContext.Response.StatusCode = 403;
                context.Result = new ViewResult
                {
                    ViewName = "~/Views/Shared/AccessDenied.cshtml",
                    StatusCode = 403
                };
                return;
            }

            await next();
        }
    }

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
    public class PermissionAuthorizeAttribute : TypeFilterAttribute
    {
        public PermissionAuthorizeAttribute() : base(typeof(PermissionAuthorizeFilter))
        {
        }
    }
}
