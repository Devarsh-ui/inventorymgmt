using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using InventoryManagementSystem.Interfaces;
using InventoryManagementSystem.Models;
using InventoryManagementSystem.ViewModels;
using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;

using InventoryManagementSystem.Data;
using MongoDB.Driver;

namespace InventoryManagementSystem.Controllers
{
    public class AccountController : Controller
    {
        private readonly IAuthService _authService;
        private readonly IImageService _imageService;
        private readonly IAuditLogService _auditLogService;
        private readonly IEmailService _emailService;
        private readonly IUserRepository _userRepository;
        private readonly IPermissionService _permissionService;
        private readonly ISupplierService _supplierService;
        private readonly IPasswordPolicyService _passwordPolicyService;
        private readonly IPasswordResetService _passwordResetService;
        private readonly MongoDbContext _context;
        private readonly ILicenseService _licenseService;

        public AccountController(
            IAuthService authService,
            IImageService imageService,
            IAuditLogService auditLogService,
            IEmailService emailService,
            IUserRepository userRepository,
            IPermissionService permissionService,
            ISupplierService supplierService,
            IPasswordPolicyService passwordPolicyService,
            IPasswordResetService passwordResetService,
            MongoDbContext context,
            ILicenseService licenseService)
        {
            _authService = authService;
            _imageService = imageService;
            _auditLogService = auditLogService;
            _emailService = emailService;
            _userRepository = userRepository;
            _permissionService = permissionService;
            _supplierService = supplierService;
            _passwordPolicyService = passwordPolicyService;
            _passwordResetService = passwordResetService;
            _context = context;
            _licenseService = licenseService;
        }

        [HttpGet]
        public IActionResult Login(string? returnUrl = null)
        {
            if (User.Identity?.IsAuthenticated == true)
            {
                if (User.IsInRole(Role.SuperAdmin))
                {
                    return RedirectToAction("Dashboard", "PlatformAdmin");
                }
                if (User.IsInRole(Role.Supplier))
                {
                    return RedirectToAction("Index", "SupplierDashboard");
                }
                return RedirectToAction("Index", "Home");
            }
            ViewData["ReturnUrl"] = returnUrl;
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(LoginViewModel model, string? returnUrl = null)
        {
            ViewData["ReturnUrl"] = returnUrl;
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            // 1. Try Authenticating User (Admin / Staff)
            var user = await _authService.AuthenticateAsync(model.UsernameOrEmail, model.Password);
            if (user != null)
            {
                // Tenant Status & License Enforcement on Login
                if (user.Role != Role.SuperAdmin && !string.IsNullOrEmpty(user.TenantId))
                {
                    var tenant = await _context.Tenants.Find(t => t.Id == user.TenantId || t.TenantCode == user.TenantId).FirstOrDefaultAsync();
                    if (tenant != null)
                    {
                        var license = await _licenseService.GetActiveLicenseAsync(tenant.Id);
                        bool isSusp = string.Equals(tenant.Status, "Suspended", StringComparison.OrdinalIgnoreCase)
                                   || (license != null && string.Equals(license.Status, "Suspended", StringComparison.OrdinalIgnoreCase));

                        if (isSusp)
                        {
                            if (user.Role == Role.Admin)
                            {
                                ModelState.AddModelError(string.Empty, "Your shop account has been suspended by the SaaS Platform Administrator. Please contact SaaS Platform Support.");
                            }
                            else
                            {
                                ModelState.AddModelError(string.Empty, "Your shop account is currently suspended. Please contact your shop administrator or management.");
                            }
                            return View(model);
                        }

                        bool isExp = string.Equals(tenant.Status, "Expired", StringComparison.OrdinalIgnoreCase)
                                  || license == null
                                  || license.ExpiryDate < DateTime.UtcNow
                                  || string.Equals(license.Status, "Expired", StringComparison.OrdinalIgnoreCase);

                        if (isExp && user.Role != Role.Admin)
                        {
                            ModelState.AddModelError(string.Empty, "Your shop's subscription license has expired. Please contact your shop administrator to renew the license.");
                            return View(model);
                        }
                    }
                }

                var claims = new List<Claim>
                {
                    new Claim(ClaimTypes.NameIdentifier, user.Id),
                    new Claim(ClaimTypes.Name, user.Username),
                    new Claim(ClaimTypes.Email, user.Email),
                    new Claim(ClaimTypes.Role, user.Role),
                    new Claim("TenantId", user.TenantId ?? ""),
                    new Claim(ClaimTypes.GroupSid, user.TenantId ?? ""),
                    new Claim("FullName", user.FullName),
                    new Claim("EmployeeId", !string.IsNullOrEmpty(user.EmployeeId) ? user.EmployeeId : "EMP-0000"),
                    new Claim("ProfilePictureUrl", string.IsNullOrEmpty(user.ProfilePictureUrl) ? "/images/default-avatar.png" : user.ProfilePictureUrl)
                };

                if (user.Permissions != null)
                {
                    foreach (var perm in user.Permissions)
                    {
                        claims.Add(new Claim("Permission", perm));
                    }
                }

                var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
                var authProperties = new AuthenticationProperties
                {
                    IsPersistent = model.RememberMe,
                    ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(120)
                };

                await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(claimsIdentity), authProperties);
                await _auditLogService.LogExAsync(user.Role == Role.Admin ? "Admin Login" : "Employee Login", "Authentication", $"{user.FullName} (@{user.Username})", $"User authenticated successfully with role '{user.Role}'.", "Success", "Success", referenceId: user.Id);

                TempData["ToastMessage"] = $"Welcome back, {user.FullName}!";
                TempData["ToastType"] = "success";

                if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl)) return Redirect(returnUrl);
                if (user.Role == Role.SuperAdmin)
                {
                    return RedirectToAction("Dashboard", "PlatformAdmin");
                }
                return RedirectToAction("Index", "Home");
            }

            // 2. Try Authenticating Supplier Vendor Account
            var supplier = await _supplierService.AuthenticateSupplierAsync(model.UsernameOrEmail, model.Password);
            if (supplier != null)
            {
                // Supplier Tenant Suspension & License Validation on Login
                if (!string.IsNullOrEmpty(supplier.TenantId))
                {
                    var tenant = await _context.Tenants.Find(t => t.Id == supplier.TenantId || t.TenantCode == supplier.TenantId).FirstOrDefaultAsync();
                    if (tenant != null)
                    {
                        var license = await _licenseService.GetActiveLicenseAsync(tenant.Id);
                        bool isSusp = string.Equals(tenant.Status, "Suspended", StringComparison.OrdinalIgnoreCase)
                                   || (license != null && string.Equals(license.Status, "Suspended", StringComparison.OrdinalIgnoreCase));

                        if (isSusp)
                        {
                            ModelState.AddModelError(string.Empty, "The shop associated with your vendor account is currently suspended. Please contact the shop administration.");
                            return View(model);
                        }

                        bool isExp = string.Equals(tenant.Status, "Expired", StringComparison.OrdinalIgnoreCase)
                                  || license == null
                                  || license.ExpiryDate < DateTime.UtcNow
                                  || string.Equals(license.Status, "Expired", StringComparison.OrdinalIgnoreCase);

                        if (isExp)
                        {
                            ModelState.AddModelError(string.Empty, "The subscription license for the associated shop has expired. Please contact the shop administration.");
                            return View(model);
                        }
                    }
                }

                var supplierClaims = new List<Claim>
                {
                    new Claim(ClaimTypes.NameIdentifier, supplier.Id),
                    new Claim(ClaimTypes.Name, supplier.CompanyName),
                    new Claim(ClaimTypes.Email, supplier.Email),
                    new Claim(ClaimTypes.Role, Role.Supplier),
                    new Claim("TenantId", supplier.TenantId ?? ""),
                    new Claim(ClaimTypes.GroupSid, supplier.TenantId ?? ""),
                    new Claim("FullName", string.IsNullOrEmpty(supplier.ContactPerson) ? supplier.CompanyName : supplier.ContactPerson),
                    new Claim("CompanyName", supplier.CompanyName),
                    new Claim("SupplierId", supplier.Id)
                };

                var claimsIdentity = new ClaimsIdentity(supplierClaims, CookieAuthenticationDefaults.AuthenticationScheme);
                var authProperties = new AuthenticationProperties
                {
                    IsPersistent = model.RememberMe,
                    ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(120)
                };

                await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(claimsIdentity), authProperties);
                await _auditLogService.LogExAsync("SUPPLIER_LOGIN", "Authentication", supplier.CompanyName, $"Supplier '{supplier.CompanyName}' ({supplier.Email}) logged in successfully.", "Success", "Success", referenceId: supplier.Id);

                TempData["ToastMessage"] = $"Welcome to SIMS Supplier Portal, {supplier.CompanyName}!";
                TempData["ToastType"] = "success";

                if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl)) return Redirect(returnUrl);
                return RedirectToAction("Index", "SupplierDashboard");
            }

            await _auditLogService.LogExAsync(
                "Failed Login", "Authentication", model.UsernameOrEmail,
                $"Failed login attempt for identifier '{model.UsernameOrEmail}'. Invalid credentials or account deactivated.",
                "Failed", "Warning");

            ModelState.AddModelError(string.Empty, "Invalid login attempt. Check credentials or account status.");
            return View(model);
        }

        [HttpGet]
        public async Task<IActionResult> Logout()
        {
            if (User.Identity?.IsAuthenticated == true)
            {
                var username = User.Identity.Name ?? "Unknown";
                await _auditLogService.LogExAsync("Employee Logout", "Authentication", username, "User terminated session and logged out.", "Success", "Information");
            }

            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            TempData["ToastMessage"] = "Logged out successfully.";
            TempData["ToastType"] = "info";

            return RedirectToAction(nameof(Login));
        }

        [HttpGet]
        public IActionResult ForgotPassword()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ForgotPassword(ForgotPasswordViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var baseUrl = $"{Request.Scheme}://{Request.Host}";
            await _passwordResetService.RequestPasswordResetByEmailAsync(model.Email, baseUrl);

            // Anti-enumeration generic response
            TempData["ToastMessage"] = "If an account exists with that email address, a password reset link has been sent.";
            TempData["ToastType"] = "info";

            return RedirectToAction(nameof(Login));
        }

        [HttpGet]
        public async Task<IActionResult> ResetPassword(string? email, string? otp, string? token)
        {
            var model = new ResetPasswordViewModel
            {
                Email = email ?? string.Empty,
                Otp = !string.IsNullOrWhiteSpace(otp) ? otp : string.Empty,
                Token = !string.IsNullOrWhiteSpace(token) ? token : string.Empty
            };

            if (!string.IsNullOrWhiteSpace(token))
            {
                var (isValid, errorMessage, request) = await _passwordResetService.ValidateResetTokenAsync(token);
                if (!isValid)
                {
                    model.IsInvalidOrExpired = true;
                    model.ErrorMessage = errorMessage ?? "This password reset link is invalid or has expired.";
                    return View(model);
                }

                if (request != null && !string.IsNullOrEmpty(request.Email))
                {
                    model.Email = request.Email;
                }
            }

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResetPassword(ResetPasswordViewModel model)
        {
            if (!string.IsNullOrWhiteSpace(model.Token))
            {
                ModelState.Remove(nameof(model.Otp));
                ModelState.Remove(nameof(model.Email));

                var policyResult = _passwordPolicyService.Validate(model.Password);
                if (!policyResult.IsValid)
                {
                    foreach (var err in policyResult.Errors)
                    {
                        ModelState.AddModelError(nameof(model.Password), err);
                    }
                    return View(model);
                }

                if (model.Password != model.ConfirmPassword)
                {
                    ModelState.AddModelError(nameof(model.ConfirmPassword), "Passwords do not match.");
                    return View(model);
                }

                var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown";
                var (success, message) = await _passwordResetService.CompletePasswordResetAsync(model.Token, model.Password, model.ConfirmPassword, ip);
                if (!success)
                {
                    model.IsInvalidOrExpired = true;
                    model.ErrorMessage = message;
                    ModelState.AddModelError(string.Empty, message);
                    return View(model);
                }

                TempData["ToastMessage"] = "Your password has been reset successfully. You can now log in with your new password.";
                TempData["ToastType"] = "success";

                return RedirectToAction(nameof(Login));
            }
            else
            {
                if (string.IsNullOrWhiteSpace(model.Email))
                {
                    ModelState.AddModelError(nameof(model.Email), "Email is required.");
                }
                if (string.IsNullOrWhiteSpace(model.Otp))
                {
                    ModelState.AddModelError(nameof(model.Otp), "OTP is required.");
                }

                var policyResult = _passwordPolicyService.Validate(model.Password);
                if (!policyResult.IsValid)
                {
                    foreach (var err in policyResult.Errors)
                    {
                        ModelState.AddModelError(nameof(model.Password), err);
                    }
                }

                if (model.Password != model.ConfirmPassword)
                {
                    ModelState.AddModelError(nameof(model.ConfirmPassword), "Passwords do not match.");
                }

                if (!ModelState.IsValid)
                {
                    return View(model);
                }

                var (success, message) = await _authService.ResetPasswordWithOtpAsync(model.Email, model.Otp ?? string.Empty, model.Password);
                if (!success)
                {
                    ModelState.AddModelError(string.Empty, message);
                    TempData["ToastMessage"] = message;
                    TempData["ToastType"] = "danger";
                    return View(model);
                }

                TempData["ToastMessage"] = "Password reset successfully! You can now log in with your new password.";
                TempData["ToastType"] = "success";

                return RedirectToAction(nameof(Login));
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResendOtp(string email)
        {
            if (string.IsNullOrWhiteSpace(email))
            {
                TempData["ToastMessage"] = "Please provide your email address to resend OTP.";
                TempData["ToastType"] = "danger";
                return RedirectToAction(nameof(ForgotPassword));
            }

            var (success, message) = await _authService.GeneratePasswordResetOtpAsync(email);
            TempData["ToastMessage"] = message;
            TempData["ToastType"] = success ? "success" : "info";

            return RedirectToAction(nameof(ResetPassword), new { email = email.Trim() });
        }

        [Authorize]
        [HttpGet]
        public async Task<IActionResult> Profile()
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId)) return RedirectToAction("Login");

            var user = await _userRepository.GetByIdAsync(userId);
            if (user == null) return NotFound();

            var model = new ProfileViewModel
            {
                Username = user.Username,
                Email = user.Email,
                FullName = user.FullName,
                PhoneNumber = user.PhoneNumber,
                Role = user.Role,
                CurrentProfilePictureUrl = string.IsNullOrEmpty(user.ProfilePictureUrl) ? "/images/default-avatar.png" : user.ProfilePictureUrl
            };

            return View(model);
        }

        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Profile(ProfileViewModel model)
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId)) return RedirectToAction("Login");

            var user = await _userRepository.GetByIdAsync(userId);
            if (user == null) return NotFound();

            // Populate non-posted read-only fields for redisplaying form
            model.Username = user.Username;
            model.Email = user.Email;
            model.Role = user.Role;
            model.CurrentProfilePictureUrl = string.IsNullOrEmpty(user.ProfilePictureUrl) ? "/images/default-avatar.png" : user.ProfilePictureUrl;

            bool isPasswordChangeAttempted = !string.IsNullOrEmpty(model.CurrentPassword) || !string.IsNullOrEmpty(model.NewPassword);

            if (!isPasswordChangeAttempted)
            {
                ModelState.Remove(nameof(model.CurrentPassword));
                ModelState.Remove(nameof(model.NewPassword));
                ModelState.Remove(nameof(model.ConfirmNewPassword));
            }
            else
            {
                if (string.IsNullOrEmpty(model.CurrentPassword))
                {
                    ModelState.AddModelError(nameof(model.CurrentPassword), "Current password is required.");
                }
                if (string.IsNullOrEmpty(model.NewPassword))
                {
                    ModelState.AddModelError(nameof(model.NewPassword), "New password is required.");
                }
                else
                {
                    if (model.CurrentPassword == model.NewPassword)
                    {
                        ModelState.AddModelError(nameof(model.NewPassword), "The new password must be different from the current password.");
                    }

                    if (model.NewPassword != model.ConfirmNewPassword)
                    {
                        ModelState.AddModelError(nameof(model.ConfirmNewPassword), "Passwords do not match.");
                    }

                    var policyResult = _passwordPolicyService.Validate(model.NewPassword, currentPasswordHash: user.PasswordHash);
                    if (!policyResult.IsValid)
                    {
                        foreach (var err in policyResult.Errors)
                        {
                            ModelState.AddModelError(nameof(model.NewPassword), err);
                        }
                    }
                }

                if (!string.IsNullOrEmpty(model.CurrentPassword) && !BCrypt.Net.BCrypt.Verify(model.CurrentPassword, user.PasswordHash))
                {
                    ModelState.AddModelError(nameof(model.CurrentPassword), "Current password is incorrect.");
                }
            }

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            string? uploadedImageUrl = null;
            if (model.ProfileImage != null && model.ProfileImage.Length > 0)
            {
                var uploadResult = await _imageService.UploadImageAsync(model.ProfileImage, "users_profiles");
                if (!uploadResult.IsSuccess)
                {
                    ModelState.AddModelError(nameof(model.ProfileImage), uploadResult.ErrorMessage);
                    return View(model);
                }
                uploadedImageUrl = uploadResult.SecureUrl;
            }

            // Update basic profile details
            var profileUpdated = await _authService.UpdateProfileAsync(userId, model.FullName, model.PhoneNumber, uploadedImageUrl);
            if (!profileUpdated)
            {
                ModelState.AddModelError(string.Empty, "Failed to update profile details.");
                return View(model);
            }

            // Change password if requested
            if (isPasswordChangeAttempted && !string.IsNullOrEmpty(model.NewPassword))
            {
                user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(model.NewPassword);
                user.PermissionVersion++;
                user.UpdatedDate = DateTime.UtcNow;
                await _userRepository.UpdateAsync(user.Id, user);
                _permissionService.InvalidateUserCache(user.Id);

                await _auditLogService.LogExAsync("EMPLOYEE_PASSWORD_CHANGED", "Account", user.Username, $"User '{user.FullName}' changed their password from user profile.", "Success", "Information", referenceId: user.Id);
                await _emailService.SendPasswordChangedEmailAsync(user.Email, user.Username);
            }

            await _auditLogService.LogActivityAsync("Profile Updated", user.Username, $"User ID: {user.Id}", "User updated their profile details.");

            // Refresh authentication cookie to reflect new profile pic/fullname in UI immediately
            var refreshedUser = await _userRepository.GetByIdAsync(userId);
            if (refreshedUser != null)
            {
                var claims = new List<Claim>
                {
                    new Claim(ClaimTypes.NameIdentifier, refreshedUser.Id),
                    new Claim(ClaimTypes.Name, refreshedUser.Username),
                    new Claim(ClaimTypes.Email, refreshedUser.Email),
                    new Claim(ClaimTypes.Role, refreshedUser.Role),
                    new Claim("FullName", refreshedUser.FullName),
                    new Claim("ProfilePictureUrl", string.IsNullOrEmpty(refreshedUser.ProfilePictureUrl) ? "/images/default-avatar.png" : refreshedUser.ProfilePictureUrl)
                };

                var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
                await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(claimsIdentity));
            }

            TempData["ToastMessage"] = "Profile updated successfully!";
            TempData["ToastType"] = "success";

            return RedirectToAction(nameof(Profile));
        }

        [HttpGet]
        public IActionResult AccessDenied()
        {
            return View();
        }

        [HttpGet("/api/account/live-status")]
        [AllowAnonymous]
        public async Task<IActionResult> GetLiveStatus()
        {
            if (User?.Identity == null || !User.Identity.IsAuthenticated)
            {
                return Ok(new { authenticated = false, isLocked = false, isDeleted = false, version = 0 });
            }

            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
            {
                return Ok(new { authenticated = false, isLocked = false, isDeleted = false, version = 0 });
            }

            if (User.IsInRole(Role.Supplier))
            {
                var supplier = await _supplierService.GetSupplierByIdAsync(userId);
                if (supplier == null || supplier.Status == "Inactive")
                {
                    return Ok(new { authenticated = true, isLocked = supplier?.Status == "Inactive", isDeleted = supplier == null, version = 1 });
                }
                return Ok(new { authenticated = true, isLocked = false, isDeleted = false, version = 1 });
            }

            var state = await _permissionService.GetLiveUserStateAsync(userId);
            return Ok(new
            {
                authenticated = true,
                isLocked = state.IsLocked,
                isDeleted = !state.IsValid,
                version = state.PermissionVersion
            });
        }
    }
}
