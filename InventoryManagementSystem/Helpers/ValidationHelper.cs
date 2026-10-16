using System;
using System.Text.RegularExpressions;

namespace InventoryManagementSystem.Helpers
{
    public static class ValidationHelper
    {
        private static readonly Regex EmailRegex = new Regex(@"^[\w\.-]+@[\w\.-]+\.\w{2,}$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex PhoneRegex = new Regex(@"^\+?[0-9\s\-]{10,15}$", RegexOptions.Compiled);
        private static readonly Regex ImeiRegex = new Regex(@"^\d{14,16}$", RegexOptions.Compiled);
        private static readonly Regex GstinRegex = new Regex(@"^[0-9]{2}[A-Z]{5}[0-9]{4}[A-Z]{1}[1-9A-Z]{1}Z[0-9A-Z]{1}$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>
        /// Validates Indian GSTIN format (15 characters: 2 state digits + 10 PAN alphanumeric + 1 entity + Z + 1 check digit).
        /// Returns true if null/empty (optional) unless required is true. Emojis and special characters are rejected.
        /// </summary>
        public static bool IsValidGstin(string? gstin, bool required = false)
        {
            if (string.IsNullOrWhiteSpace(gstin)) return !required;
            var clean = gstin.Trim();
            if (clean.Length != 15) return false;
            return GstinRegex.IsMatch(clean);
        }

        /// <summary>
        /// Validates email address format. Returns true if null/empty or valid RFC format.
        /// </summary>
        public static bool IsValidEmail(string? email, bool required = false)
        {
            if (string.IsNullOrWhiteSpace(email)) return !required;
            return EmailRegex.IsMatch(email.Trim());
        }

        /// <summary>
        /// Validates phone / contact number format (10 to 15 digits). Returns true if null/empty or valid format.
        /// </summary>
        public static bool IsValidPhone(string? phone, bool required = false)
        {
            if (string.IsNullOrWhiteSpace(phone)) return !required;
            var clean = phone.Trim();
            // Count digits
            int digitCount = 0;
            foreach (char c in clean)
            {
                if (char.IsDigit(c)) digitCount++;
            }
            return PhoneRegex.IsMatch(clean) && digitCount >= 10 && digitCount <= 15;
        }

        /// <summary>
        /// Validates IMEI number (strictly 14-16 numeric digits, standard 15-digit IMEI).
        /// </summary>
        public static bool IsValidImei(string? imei, bool required = false)
        {
            if (string.IsNullOrWhiteSpace(imei)) return !required;
            return ImeiRegex.IsMatch(imei.Trim());
        }

        private static readonly Regex PasswordRegex = new Regex(@"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[^A-Za-z0-9]).{8,}$", RegexOptions.Compiled);

        /// <summary>
        /// Validates password against the unified SIMS security policy.
        /// </summary>
        public static bool IsValidPassword(string? password, out System.Collections.Generic.List<string> errors, string? confirmPassword = null, string? currentPasswordHash = null)
        {
            var policyService = new InventoryManagementSystem.Services.PasswordPolicyService();
            var result = policyService.Validate(password, confirmPassword, currentPasswordHash, confirmPassword != null);
            errors = result.Errors;
            return result.IsValid;
        }

        /// <summary>
        /// Quick boolean check for valid password meeting SIMS security policy.
        /// </summary>
        public static bool IsValidPassword(string? password)
        {
            if (string.IsNullOrWhiteSpace(password)) return false;
            return PasswordRegex.IsMatch(password.Trim());
        }
    }
}
