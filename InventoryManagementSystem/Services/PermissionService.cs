using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using InventoryManagementSystem.Interfaces;
using InventoryManagementSystem.Models;

namespace InventoryManagementSystem.Services
{
    public class PermissionService : IPermissionService
    {
        private readonly IUserRepository _userRepository;
        private readonly ISupplierService _supplierService;
        private readonly IPermissionDiscoveryService _permissionDiscovery;
        private readonly IMemoryCache _cache;

        private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

        public PermissionService(
            IUserRepository userRepository,
            ISupplierService supplierService,
            IPermissionDiscoveryService permissionDiscovery,
            IMemoryCache cache)
        {
            _userRepository = userRepository;
            _supplierService = supplierService;
            _permissionDiscovery = permissionDiscovery;
            _cache = cache;
        }

        public async Task<LiveUserState> GetLiveUserStateAsync(string userId)
        {
            if (string.IsNullOrWhiteSpace(userId))
                return new LiveUserState { IsValid = false };

            string cacheKey = $"live_user_state_{userId}";

            if (_cache.TryGetValue(cacheKey, out LiveUserState? cachedState) && cachedState != null)
            {
                return cachedState;
            }

            var user = await _userRepository.GetByIdAsync(userId);
            if (user == null)
            {
                var supplier = await _supplierService.GetSupplierByIdAsync(userId);
                if (supplier != null)
                {
                    var supplierState = new LiveUserState
                    {
                        IsValid = supplier.Status == "Active",
                        IsLocked = supplier.Status == "Inactive",
                        PermissionVersion = 1,
                        Role = Role.Supplier,
                        Permissions = new List<string> { "SupplierDashboard.Index", "SupplierDashboard.Products", "SupplierDashboard.Orders", "SupplierDashboard.Inventory", "SupplierDashboard.Stats", "SupplierDashboard.PurchaseReturns", "SupplierDashboard.Profile", "Category.Index" }
                    };
                    _cache.Set(cacheKey, supplierState, CacheDuration);
                    return supplierState;
                }

                var invalidState = new LiveUserState { IsValid = false };
                _cache.Set(cacheKey, invalidState, TimeSpan.FromSeconds(30));
                return invalidState;
            }

            List<string> permissions;
            if (user.Role.Equals(Role.Admin, StringComparison.OrdinalIgnoreCase))
            {
                permissions = _permissionDiscovery.DiscoverAllPermissions().Select(p => p.PermissionKey).ToList();
            }
            else
            {
                // Strip any legacy Admin.* or User.* permission keys for employee accounts
                permissions = (user.Permissions ?? new List<string>())
                    .Where(p => !p.StartsWith("Admin.", StringComparison.OrdinalIgnoreCase) &&
                                !p.StartsWith("User.", StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            var liveState = new LiveUserState
            {
                IsValid = true,
                IsLocked = user.IsLocked,
                PermissionVersion = user.PermissionVersion > 0 ? user.PermissionVersion : 1,
                Role = user.Role,
                Permissions = permissions
            };

            _cache.Set(cacheKey, liveState, CacheDuration);
            return liveState;
        }

        public void InvalidateUserCache(string userId)
        {
            if (!string.IsNullOrWhiteSpace(userId))
            {
                string cacheKey = $"live_user_state_{userId}";
                _cache.Remove(cacheKey);
            }
        }

        public async Task<bool> HasLivePermissionAsync(ClaimsPrincipal user, string controllerName, string actionName)
        {
            if (user?.Identity == null || !user.Identity.IsAuthenticated)
                return false;

            // Administrator Management (Admin) and Employee Management (User) are strictly restricted to Admin role
            if (string.Equals(controllerName, "Admin", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(controllerName, "User", StringComparison.OrdinalIgnoreCase))
            {
                return user.IsInRole(Role.Admin);
            }

            if (user.IsInRole(Role.Admin))
                return true;

            var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
                return false;

            var state = await GetLiveUserStateAsync(userId);
            if (!state.IsValid || state.IsLocked)
                return false;

            var key = _permissionDiscovery.GetPermissionKey(controllerName, actionName);

            if (state.Permissions.Contains(key, StringComparer.OrdinalIgnoreCase))
                return true;

            // Fallback for Index / View controller-level access check
            if (actionName.Equals("Index", StringComparison.OrdinalIgnoreCase) || actionName.Equals("View", StringComparison.OrdinalIgnoreCase))
            {
                return state.Permissions.Any(p => p.StartsWith($"{controllerName}.", StringComparison.OrdinalIgnoreCase));
            }

            return false;
        }

        public bool HasPermission(ClaimsPrincipal user, string permissionKey)
        {
            if (user?.Identity == null || !user.Identity.IsAuthenticated)
                return false;

            if (user.IsInRole(Role.Admin))
                return true;

            var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!string.IsNullOrEmpty(userId))
            {
                var stateTask = GetLiveUserStateAsync(userId);
                var state = stateTask.ConfigureAwait(false).GetAwaiter().GetResult();
                if (!state.IsValid || state.IsLocked) return false;

                if (state.Permissions.Contains(permissionKey, StringComparer.OrdinalIgnoreCase))
                    return true;

                var parts = permissionKey.Split('.');
                if (parts.Length == 2 && (parts[1].Equals("Index", StringComparison.OrdinalIgnoreCase) || parts[1].Equals("View", StringComparison.OrdinalIgnoreCase)))
                {
                    return state.Permissions.Any(p => p.StartsWith($"{parts[0]}.", StringComparison.OrdinalIgnoreCase));
                }
                return false;
            }

            var permissionClaims = user.FindAll("Permission").Select(c => c.Value).ToList();
            return permissionClaims.Contains(permissionKey, StringComparer.OrdinalIgnoreCase);
        }

        public bool HasPermission(ClaimsPrincipal user, string controllerName, string actionName)
        {
            var key = _permissionDiscovery.GetPermissionKey(controllerName, actionName);
            return HasPermission(user, key);
        }

        public bool HasModuleAccess(ClaimsPrincipal user, string controllerName)
        {
            if (user?.Identity == null || !user.Identity.IsAuthenticated)
                return false;

            if (user.IsInRole(Role.Admin))
                return true;

            return HasPermission(user, controllerName, "Index");
        }

        public async Task<List<string>> GetUserPermissionsAsync(string userId)
        {
            var state = await GetLiveUserStateAsync(userId);
            return state.Permissions;
        }

        public async Task<bool> UpdateUserPermissionsAsync(string userId, List<string> permissions)
        {
            if (string.IsNullOrWhiteSpace(userId)) return false;

            var user = await _userRepository.GetByIdAsync(userId);
            if (user == null) return false;

            user.Permissions = permissions ?? new List<string>();
            user.PermissionVersion++;
            user.LastPermissionUpdated = DateTime.UtcNow;
            user.UpdatedDate = DateTime.UtcNow;

            await _userRepository.UpdateAsync(user.Id, user);
            InvalidateUserCache(user.Id);
            return true;
        }
    }
}
