using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using InventoryManagementSystem.Configuration;
using InventoryManagementSystem.Data;
using InventoryManagementSystem.Extensions;
using InventoryManagementSystem.Interfaces;
using InventoryManagementSystem.Models;
using MongoDB.Driver;
using System;
using System.Text;
using System.Threading.Tasks;

namespace InventoryManagementSystem.Services
{
    public class EmailService : IEmailService
    {
        private readonly BrevoSettings _settings;
        private readonly IBrevoEmailService _brevoApiEmailService;
        private readonly MongoDbContext _context;
        private readonly IWebHostEnvironment _env;
        private readonly ILogger<EmailService> _logger;

        public EmailService(
            IOptions<BrevoSettings> settings,
            IBrevoEmailService brevoApiEmailService,
            MongoDbContext context,
            IWebHostEnvironment env,
            ILogger<EmailService> logger)
        {
            _settings = settings.Value;
            _brevoApiEmailService = brevoApiEmailService;
            _context = context;
            _env = env;
            _logger = logger;
        }

        public async Task SendEmailAsync(string toEmail, string subject, string htmlMessage)
        {
            // 1. First check if Brevo REST API can be used (if ApiKey is set)
            var apiKey = Environment.GetEnvironmentVariable("BREVO_API_KEY");
            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                var (success, _, _, error) = await _brevoApiEmailService.SendTransactionalEmailAsync(toEmail, subject, htmlMessage);
                if (success) return;
                _logger.LogWarning("Brevo REST API email attempt failed: {Error}. Trying SMTP fallback.", error);
            }

            // 2. Validate SMTP credentials before attempting connection
            if (string.IsNullOrWhiteSpace(_settings.Host) || string.IsNullOrWhiteSpace(_settings.Username) || string.IsNullOrWhiteSpace(_settings.Password))
            {
                _logger.LogWarning("[EMAIL] SMTP credentials (Host/Username/Password) are not configured in appsettings.json. Skipping SMTP attempt.");
                if (_env.IsDevelopment())
                {
                    Console.WriteLine($"[EMAIL NOTICE] Cannot send email to {toEmail} via SMTP: Username or Password is empty in appsettings.json -> BrevoSettings.");
                }
                return;
            }

            // 3. Send via MailKit SMTP
            var emailMessage = new MimeMessage();
            emailMessage.From.Add(new MailboxAddress(_settings.FromName, _settings.FromEmail));
            emailMessage.To.Add(new MailboxAddress("", toEmail));
            emailMessage.Subject = subject;

            var bodyBuilder = new BodyBuilder { HtmlBody = htmlMessage };
            emailMessage.Body = bodyBuilder.ToMessageBody();

            using var client = new SmtpClient();
            try
            {
                await client.ConnectAsync(_settings.Host, _settings.Port, SecureSocketOptions.StartTls);
                await client.AuthenticateAsync(_settings.Username, _settings.Password);
                await client.SendAsync(emailMessage);
                _logger.LogInformation("[EMAIL] Email successfully sent via SMTP to {Email}", toEmail);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Email sending failed: {Message}", ex.Message);
                Console.WriteLine($"Email sending failed: {ex.Message}");
            }
            finally
            {
                try
                {
                    await client.DisconnectAsync(true);
                }
                catch { }
            }
        }

        public async Task SendEmailWithAttachmentAsync(string toEmail, string subject, string htmlMessage, byte[] attachmentBytes, string attachmentFileName, string contentType = "application/pdf")
        {
            // 1. First check if Brevo REST API can be used (if ApiKey is set)
            var apiKey = Environment.GetEnvironmentVariable("BREVO_API_KEY");
            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                var (success, _, _, error) = await _brevoApiEmailService.SendTransactionalEmailAsync(toEmail, subject, htmlMessage, null, attachmentFileName, attachmentBytes);
                if (success) return;
                _logger.LogWarning("Brevo REST API email with attachment attempt failed: {Error}. Trying SMTP fallback.", error);
            }

            // 2. Validate SMTP credentials before attempting connection
            if (string.IsNullOrWhiteSpace(_settings.Host) || string.IsNullOrWhiteSpace(_settings.Username) || string.IsNullOrWhiteSpace(_settings.Password))
            {
                _logger.LogWarning("[EMAIL] SMTP credentials (Host/Username/Password) are not configured in appsettings.json. Skipping SMTP attempt.");
                if (_env.IsDevelopment())
                {
                    Console.WriteLine($"[EMAIL NOTICE] Cannot send email with attachment to {toEmail} via SMTP: Username or Password is empty in appsettings.json -> BrevoSettings.");
                }
                return;
            }

            // 3. Send via MailKit SMTP with attachment
            var emailMessage = new MimeMessage();
            emailMessage.From.Add(new MailboxAddress(_settings.FromName, _settings.FromEmail));
            emailMessage.To.Add(new MailboxAddress("", toEmail));
            emailMessage.Subject = subject;

            var bodyBuilder = new BodyBuilder { HtmlBody = htmlMessage };
            if (attachmentBytes != null && attachmentBytes.Length > 0)
            {
                bodyBuilder.Attachments.Add(attachmentFileName ?? "Invoice.pdf", attachmentBytes, ContentType.Parse(contentType ?? "application/pdf"));
            }
            emailMessage.Body = bodyBuilder.ToMessageBody();

            using var client = new SmtpClient();
            try
            {
                await client.ConnectAsync(_settings.Host, _settings.Port, SecureSocketOptions.StartTls);
                await client.AuthenticateAsync(_settings.Username, _settings.Password);
                await client.SendAsync(emailMessage);
                _logger.LogInformation("[EMAIL] Email with attachment successfully sent via SMTP to {Email}", toEmail);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Email sending with attachment failed: {Message}", ex.Message);
                Console.WriteLine($"Email sending with attachment failed: {ex.Message}");
            }
            finally
            {
                try
                {
                    await client.DisconnectAsync(true);
                }
                catch { }
            }
        }

        public async Task SendInvoiceEmailAsync(string toEmail, Sale sale, byte[] pdfBytes)
        {
            Settings? settings = null;
            if (!string.IsNullOrEmpty(sale.TenantId))
            {
                settings = await _context.Settings.Find(s => s.TenantId == sale.TenantId).FirstOrDefaultAsync();
            }
            if (settings == null)
            {
                settings = await _context.Settings.Find(s => s.TenantId == null || s.TenantId == "").FirstOrDefaultAsync() ?? new Settings();
            }

            Tenant? tenant = null;
            if (!string.IsNullOrEmpty(sale.TenantId))
            {
                tenant = await _context.Tenants.Find(t => t.Id == sale.TenantId).FirstOrDefaultAsync();
            }

            var companyName = !string.IsNullOrWhiteSpace(settings.CompanyName) ? settings.CompanyName : (tenant?.ShopName ?? "SIMS SMART INVENTORY SYSTEM");
            var companyAddress = !string.IsNullOrWhiteSpace(settings.Address) ? settings.Address : (tenant?.Address ?? "Central Electronics Hub, India");
            var companyPhone = !string.IsNullOrWhiteSpace(settings.CompanyPhone) ? settings.CompanyPhone : (tenant?.Phone ?? "+91 98765 43210");
            var companyEmail = !string.IsNullOrWhiteSpace(settings.CompanyEmail) ? settings.CompanyEmail : (tenant?.ContactEmail ?? "support@sims.com");
            var companyGstin = !string.IsNullOrWhiteSpace(sale.CompanyGstin) ? sale.CompanyGstin : (!string.IsNullOrWhiteSpace(settings.GstinNumber) ? settings.GstinNumber : (tenant?.GSTIN ?? "27AAAAA0000A1Z5"));

            var statusBadgeColor = sale.PaymentStatus switch
            {
                "Paid" => "#10b981",
                "Partial" => "#f59e0b",
                "Draft" => "#6b7280",
                _ => "#ef4444"
            };

            var formattedDate = sale.Date.ToIstString("dd-MMM-yyyy HH:mm IST");

            var itemsHtml = new StringBuilder();
            foreach (var item in sale.Items)
            {
                var imeiHtml = string.Empty;
                if (!string.IsNullOrWhiteSpace(item.IMEI1))
                {
                    imeiHtml = $"<div style='font-size:11px;color:#94a3b8;font-family:monospace;'>IMEI: {item.IMEI1}</div>";
                }
                else if (!string.IsNullOrWhiteSpace(item.SerialNumber))
                {
                    imeiHtml = $"<div style='font-size:11px;color:#94a3b8;font-family:monospace;'>SN: {item.SerialNumber}</div>";
                }

                itemsHtml.Append($@"
                <tr style='border-bottom:1px solid #334155;'>
                    <td style='padding:12px 8px;'>
                        <div style='font-weight:600;color:#f8fafc;'>{item.ProductName}</div>
                        <div style='font-size:12px;color:#94a3b8;'>{item.Brand} {item.ModelName} {(string.IsNullOrEmpty(item.Variant) ? "" : "· " + item.Variant)} {(string.IsNullOrEmpty(item.Color) ? "" : "· " + item.Color)}</div>
                        {imeiHtml}
                    </td>
                    <td style='padding:12px 8px;text-align:center;color:#cbd5e1;'>{item.Quantity}</td>
                    <td style='padding:12px 8px;text-align:right;color:#cbd5e1;'>₹{item.SellingPrice:N2}</td>
                    <td style='padding:12px 8px;text-align:right;font-weight:700;color:#10b981;'>₹{item.Total:N2}</td>
                </tr>");
            }

            var discountsHtml = string.Empty;
            if (sale.Discount > 0 || sale.ExchangeDiscount > 0)
            {
                discountsHtml = $@"
                <tr>
                    <td colspan='3' style='padding:6px 8px;text-align:right;color:#94a3b8;'>Discounts Applied:</td>
                    <td style='padding:6px 8px;text-align:right;color:#f59e0b;font-weight:600;'>- ₹{(sale.Discount + sale.ExchangeDiscount):N2}</td>
                </tr>";
            }

            var emailHtml = $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8' />
    <title>Tax Invoice #{sale.InvoiceNumber}</title>
    <style>
        body {{ font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif; background-color: #0f172a; color: #f8fafc; margin: 0; padding: 20px; }}
        .wrapper {{ max-width: 650px; margin: 0 auto; background-color: #1e293b; border-radius: 12px; overflow: hidden; border: 1px solid #334155; box-shadow: 0 10px 25px rgba(0,0,0,0.5); }}
        .header {{ background: linear-gradient(135deg, #1e3a8a 0%, #2563eb 100%); padding: 30px; text-align: left; }}
        .content {{ padding: 30px; }}
        .badge {{ display: inline-block; padding: 4px 10px; border-radius: 6px; font-size: 11px; font-weight: 700; text-transform: uppercase; }}
        .summary-table {{ width: 100%; border-collapse: collapse; margin-top: 15px; }}
        .footer {{ background-color: #0f172a; padding: 20px 30px; text-align: center; font-size: 12px; color: #64748b; border-top: 1px solid #334155; }}
    </style>
</head>
<body>
    <div class='wrapper'>
        <div class='header'>
            <table style='width:100%;'>
                <tr>
                    <td>
                        <h2 style='margin:0;color:#ffffff;font-size:22px;font-weight:800;letter-spacing:0.5px;'>{companyName.ToUpper()}</h2>
                        <div style='color:#cbd5e1;font-size:12px;margin-top:4px;'>{companyAddress}</div>
                        <div style='color:#93c5fd;font-size:12px;'>Ph: {companyPhone} · GSTIN: {companyGstin}</div>
                    </td>
                    <td style='text-align:right;vertical-align:top;'>
                        <div style='background:rgba(255,255,255,0.15);padding:6px 12px;border-radius:6px;display:inline-block;'>
                            <div style='font-size:11px;color:#bfdbfe;text-transform:uppercase;'>Invoice Number</div>
                            <div style='font-size:16px;font-weight:800;color:#ffffff;font-family:monospace;'>{sale.InvoiceNumber}</div>
                        </div>
                    </td>
                </tr>
            </table>
        </div>

        <div class='content'>
            <div style='border-bottom:1px solid #334155;padding-bottom:15px;margin-bottom:20px;'>
                <table style='width:100%;'>
                    <tr>
                        <td style='vertical-align:top;'>
                            <div style='font-size:11px;color:#94a3b8;text-transform:uppercase;font-weight:700;'>Billed To</div>
                            <div style='font-size:16px;font-weight:700;color:#f8fafc;margin-top:2px;'>{(string.IsNullOrWhiteSpace(sale.CustomerName) ? "Walk-in Customer" : sale.CustomerName)}</div>
                            {(string.IsNullOrWhiteSpace(sale.CustomerPhone) ? "" : $"<div style='font-size:13px;color:#cbd5e1;'>Phone: {sale.CustomerPhone}</div>")}
                            <div style='font-size:13px;color:#cbd5e1;'>Email: {toEmail}</div>
                        </td>
                        <td style='vertical-align:top;text-align:right;'>
                            <div style='font-size:11px;color:#94a3b8;text-transform:uppercase;font-weight:700;'>Invoice Details</div>
                            <div style='font-size:13px;color:#cbd5e1;margin-top:2px;'>Date: <strong>{formattedDate}</strong></div>
                            <div style='font-size:13px;color:#cbd5e1;'>Payment: <strong>{sale.PaymentMethod}</strong></div>
                            <div style='margin-top:4px;'><span class='badge' style='background:{statusBadgeColor}22;color:{statusBadgeColor};border:1px solid {statusBadgeColor};'>{sale.PaymentStatus}</span></div>
                        </td>
                    </tr>
                </table>
            </div>

            <!-- Items Table -->
            <table class='summary-table' style='width:100%;'>
                <thead>
                    <tr style='background:#0f172a;color:#94a3b8;font-size:12px;text-transform:uppercase;'>
                        <th style='padding:10px 8px;text-align:left;border-radius:6px 0 0 6px;'>Item Description</th>
                        <th style='padding:10px 8px;text-align:center;'>Qty</th>
                        <th style='padding:10px 8px;text-align:right;'>Unit Price</th>
                        <th style='padding:10px 8px;text-align:right;border-radius:0 6px 6px 0;'>Amount</th>
                    </tr>
                </thead>
                <tbody>
                    {itemsHtml}
                </tbody>
                <tfoot>
                    <tr>
                        <td colspan='3' style='padding:12px 8px 4px;text-align:right;color:#94a3b8;'>Subtotal:</td>
                        <td style='padding:12px 8px 4px;text-align:right;color:#f8fafc;font-weight:600;'>₹{sale.SubTotal:N2}</td>
                    </tr>
                    {discountsHtml}
                    <tr>
                        <td colspan='3' style='padding:4px 8px;text-align:right;color:#94a3b8;'>GST ({sale.GstPercentage}%):</td>
                        <td style='padding:4px 8px;text-align:right;color:#f8fafc;font-weight:600;'>₹{sale.GstAmount:N2}</td>
                    </tr>
                    <tr style='border-top:1px solid #475569;'>
                        <td colspan='3' style='padding:10px 8px;text-align:right;color:#ffffff;font-size:15px;font-weight:800;'>Grand Total:</td>
                        <td style='padding:10px 8px;text-align:right;color:#10b981;font-size:18px;font-weight:800;'>₹{sale.GrandTotal:N2}</td>
                    </tr>
                    <tr>
                        <td colspan='3' style='padding:2px 8px;text-align:right;color:#94a3b8;font-size:12px;'>Amount Paid:</td>
                        <td style='padding:2px 8px;text-align:right;color:#cbd5e1;font-size:12px;'>₹{sale.AmountPaid:N2}</td>
                    </tr>
                    {(sale.DueAmount > 0 ? $"<tr><td colspan='3' style='padding:2px 8px;text-align:right;color:#ef4444;font-size:12px;font-weight:700;'>Balance Due:</td><td style='padding:2px 8px;text-align:right;color:#ef4444;font-size:12px;font-weight:700;'>₹{sale.DueAmount:N2}</td></tr>" : "")}
                </tfoot>
            </table>

            <!-- Attachment Notice Card -->
            <div style='background:rgba(37,99,235,0.1);border:1px solid rgba(37,99,235,0.3);border-radius:8px;padding:14px;margin-top:25px;'>
                <table style='width:100%;'>
                    <tr>
                        <td style='width:36px;vertical-align:top;font-size:24px;'>📎</td>
                        <td>
                            <div style='font-weight:700;color:#93c5fd;font-size:13px;'>Official Tax Invoice PDF Attached</div>
                            <div style='font-size:12px;color:#cbd5e1;margin-top:2px;'>
                                A complete PDF copy (<strong>Invoice_{sale.InvoiceNumber}.pdf</strong>) has been generated and attached to this email. Please download or save it for warranty claims and official records.
                            </div>
                        </td>
                    </tr>
                </table>
            </div>
        </div>

        <div class='footer'>
            <div style='margin-bottom:6px;'>Thank you for choosing <strong>{companyName}</strong>! We appreciate your business.</div>
            <div>For assistance or inquiries, reach out at <a href='mailto:{companyEmail}' style='color:#3b82f6;text-decoration:none;'>{companyEmail}</a> or call {companyPhone}.</div>
            <div style='margin-top:10px;font-size:11px;color:#475569;'>&copy; {DateTime.UtcNow.Year} {companyName}. Generated electronically by SIMS.</div>
        </div>
    </div>
</body>
</html>";

            var attachmentFileName = $"Invoice_{sale.InvoiceNumber}.pdf";
            var subject = $"Tax Invoice #{sale.InvoiceNumber} - {companyName}";
            await SendEmailWithAttachmentAsync(toEmail, subject, emailHtml, pdfBytes, attachmentFileName);
        }

        private string GetStandardHtmlTemplate(string title, string content)
        {
            return $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8' />
    <title>{title}</title>
    <style>
        body {{
            font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif;
            background-color: #1a1e29;
            color: #ffffff;
            margin: 0;
            padding: 0;
        }}
        .container {{
            max-width: 600px;
            margin: 20px auto;
            background-color: #0f121d;
            border-radius: 8px;
            overflow: hidden;
            box-shadow: 0 4px 15px rgba(0,0,0,0.5);
            border: 1px solid #232a3b;
        }}
        .header {{
            background: linear-gradient(135deg, #0d6efd 0%, #0a58ca 100%);
            padding: 30px;
            text-align: center;
        }}
        .header h1 {{
            margin: 0;
            color: #ffffff;
            font-size: 24px;
            font-weight: 600;
        }}
        .body {{
            padding: 40px 30px;
            line-height: 1.6;
            color: #e2e8f0;
        }}
        .footer {{
            background-color: #0c0e18;
            padding: 20px;
            text-align: center;
            font-size: 12px;
            color: #64748b;
            border-top: 1px solid #232a3b;
        }}
        .btn {{
            display: inline-block;
            background-color: #0d6efd;
            color: #ffffff !important;
            padding: 12px 25px;
            border-radius: 5px;
            text-decoration: none;
            font-weight: 600;
            margin-top: 20px;
        }}
        .warning {{
            color: #ffc107;
            font-weight: bold;
        }}
    </style>
</head>
<body>
    <div class='container'>
        <div class='header'>
            <h1>{title}</h1>
        </div>
        <div class='body'>
            {content}
        </div>
        <div class='footer'>
            &copy; {DateTime.UtcNow.Year} Smart Inventory Management System (SIMS). All rights reserved.
        </div>
    </div>
</body>
</html>";
        }

        public async Task SendWelcomeEmailAsync(string toEmail, string username)
        {
            var content = $@"
<p>Hello <strong>{username}</strong>,</p>
<p>Welcome to the <strong>Smart Inventory Management System (SIMS)</strong>! Your account has been successfully set up and is active.</p>
<p>You can now log in using your credentials to manage inventory, track products, and generate reports.</p>
<a href='#' class='btn'>Access Dashboard</a>
<p style='margin-top: 20px; font-size: 13px; color: #64748b;'>If you did not request this account, please notify the system administrator immediately.</p>";
            await SendEmailAsync(toEmail, "Welcome to SIMS!", GetStandardHtmlTemplate("Welcome to SIMS", content));
        }

        public async Task SendForgotPasswordEmailAsync(string toEmail, string resetLink)
        {
            await SendPasswordResetEmailAsync(toEmail, "User", resetLink, "Account");
        }

        public async Task SendPasswordResetEmailAsync(string toEmail, string recipientName, string resetLink, string accountType)
        {
            if (_env.IsDevelopment())
            {
                Console.WriteLine("=================================================");
                Console.WriteLine($"[SIMS DEV RESET LINK] Password reset link for {toEmail} ({accountType}): {resetLink}");
                Console.WriteLine("=================================================");
            }

            var safeName = System.Net.WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(recipientName) ? "User" : recipientName);
            var safeAccountType = System.Net.WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(accountType) ? "SIMS" : accountType);

            var content = $@"
<p>Hello <strong>{safeName}</strong>,</p>
<p>A password reset has been requested for your <strong>SIMS {safeAccountType}</strong> account.</p>
<p>Click the button below to create a new secure password:</p>
<div style='text-align: center; margin: 30px 0;'>
    <a href='{resetLink}' class='btn' style='display:inline-block;background-color:#0d6efd;color:#ffffff!important;padding:14px 28px;border-radius:6px;text-decoration:none;font-weight:700;font-size:16px;'>Reset Password</a>
</div>
<p style='font-size:13px;color:#94a3b8;'>This link is temporary and can only be used for a limited time (<strong>60 minutes</strong>). It is single-use and will be invalidated after resetting.</p>
<p style='font-size:13px;color:#94a3b8;'>If you did not expect this request, you can safely ignore this email. Your current password remains secure.</p>
<p style='margin-top: 25px; font-size:13px; color:#64748b;'>Regards,<br /><strong>Smart Inventory Management System (SIMS)</strong></p>";

            await SendEmailAsync(toEmail, "Reset Your SIMS Account Password", GetStandardHtmlTemplate("Reset Your SIMS Account Password", content));
        }

        public async Task SendPasswordResetOtpEmailAsync(string toEmail, string otpCode, string recipientName)
        {
            if (_env.IsDevelopment())
            {
                Console.WriteLine("=================================================");
                Console.WriteLine($"[SIMS DEV OTP] Verification OTP for {toEmail}: {otpCode}");
                Console.WriteLine("=================================================");
            }

            var content = $@"
<p>Hello <strong>{System.Net.WebUtility.HtmlEncode(recipientName)}</strong>,</p>
<p>We received a request to reset your password for your <strong>Smart Inventory Management System (SIMS)</strong> account.</p>
<p>Please use the following 6-digit One-Time Password (OTP) to reset your password:</p>

<div style='background-color: #1e2538; border: 2px dashed #3b82f6; border-radius: 10px; padding: 18px 24px; text-align: center; margin: 24px 0;'>
    <div style='font-size: 12px; color: #94a3b8; text-transform: uppercase; font-weight: 700; letter-spacing: 1px; margin-bottom: 6px;'>Your Verification OTP</div>
    <div style='font-size: 32px; font-weight: 800; letter-spacing: 8px; color: #38bdf8; font-family: monospace;'>{otpCode}</div>
    <div style='font-size: 12px; color: #f59e0b; margin-top: 8px; font-weight: 600;'>Valid for 15 minutes only</div>
</div>

<p style='font-size: 13px; color: #94a3b8;'>Enter this OTP on the password reset screen along with your new password (minimum 8 characters).</p>
<p style='margin-top: 20px; font-size: 12px; color: #64748b;'>If you did not request a password reset, please ignore this email or notify your system administrator immediately. Your password will remain unchanged.</p>";

            await SendEmailAsync(toEmail, $"SIMS Password Reset OTP: {otpCode}", GetStandardHtmlTemplate("Password Reset OTP", content));
        }

        public async Task SendPasswordChangedEmailAsync(string toEmail, string username)
        {
            var content = $@"
<p>Hello <strong>{username}</strong>,</p>
<p>This is to confirm that the password for your SIMS account has been successfully changed.</p>
<p class='warning'>If you did not make this change, please contact the administrator immediately to secure your account.</p>
<a href='#' class='btn'>Log In to SIMS</a>";
            await SendEmailAsync(toEmail, "Password Changed - SIMS", GetStandardHtmlTemplate("Password Changed", content));
        }

        public async Task SendLowStockAlertEmailAsync(string toEmail, string productName, int currentStock, int minStock)
        {
            var content = $@"
<p>Hello,</p>
<p class='warning'>This is an automated inventory alert from SIMS.</p>
<p>The product <strong>{productName}</strong> has fallen below its minimum stock threshold.</p>
<table style='width: 100%; border-collapse: collapse; margin-top: 15px;'>
    <tr style='background-color: #232a3b; border: 1px solid #4a5568;'>
        <th style='padding: 8px; text-align: left; color: #a0aec0;'>Metric</th>
        <th style='padding: 8px; text-align: left; color: #a0aec0;'>Value</th>
    </tr>
    <tr style='border: 1px solid #4a5568;'>
        <td style='padding: 8px;'>Current Stock</td>
        <td style='padding: 8px; color: #f56565; font-weight: bold;'>{currentStock}</td>
    </tr>
    <tr style='border: 1px solid #4a5568;'>
        <td style='padding: 8px;'>Minimum Required</td>
        <td style='padding: 8px;'>{minStock}</td>
    </tr>
</table>
<p style='margin-top: 20px;'>Please log in to increase the stock level for this item.</p>
<a href='#' class='btn'>View Inventory</a>";
            await SendEmailAsync(toEmail, $"Low Stock Alert: {productName} - SIMS", GetStandardHtmlTemplate("Low Stock Alert", content));
        }

        public async Task SendNewUserCreatedEmailAsync(string toEmail, string username, string role, string tempPassword)
        {
            var content = $@"
<p>Hello <strong>{username}</strong>,</p>
<p>Your user profile has been created by the Administrator under the role <strong>{role}</strong>.</p>
<p>You can use the temporary details below to log in for the first time. You will be prompted to change your password immediately.</p>
<table style='width: 100%; border-collapse: collapse; margin-top: 15px;'>
    <tr style='border: 1px solid #4a5568;'>
        <td style='padding: 8px; font-weight: bold;'>Username / Email</td>
        <td style='padding: 8px;'>{toEmail}</td>
    </tr>
    <tr style='border: 1px solid #4a5568;'>
        <td style='padding: 8px; font-weight: bold;'>Temporary Password</td>
        <td style='padding: 8px; font-family: monospace; background-color: #232a3b;'>{tempPassword}</td>
    </tr>
</table>
<a href='#' class='btn'>Log In to System</a>";
            await SendEmailAsync(toEmail, "Account Created - SIMS", GetStandardHtmlTemplate("SIMS Account Created", content));
        }

        public async Task SendLicenseRenewalApprovedEmailAsync(
            string toEmail, 
            string shopName, 
            string ownerName, 
            string packageName, 
            int extensionDays, 
            DateTime newExpiryDate, 
            string? adminNotes)
        {
            var safeShop = System.Net.WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(shopName) ? "Your Shop" : shopName);
            var safeOwner = System.Net.WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(ownerName) ? "Store Owner" : ownerName);
            var safePackage = System.Net.WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(packageName) ? "Standard Package" : packageName);
            var safeNotes = !string.IsNullOrWhiteSpace(adminNotes) ? System.Net.WebUtility.HtmlEncode(adminNotes) : null;
            var istExpiry = newExpiryDate.AddMinutes(330);

            var notesHtml = safeNotes != null ? $@"
<div style='background-color:#1e293b;border-left:4px solid #10b981;padding:12px 16px;border-radius:4px;margin:20px 0;'>
    <strong style='color:#10b981;font-size:13px;display:block;margin-bottom:4px;'>Admin Notes / Message:</strong>
    <span style='color:#e2e8f0;font-size:14px;line-height:1.5;'>{safeNotes}</span>
</div>" : "";

            var content = $@"
<div style='text-align:center;margin-bottom:20px;'>
    <div style='display:inline-block;background-color:rgba(16,185,129,0.15);color:#10b981;padding:8px 18px;border-radius:20px;font-weight:700;font-size:14px;border:1px solid rgba(16,185,129,0.3);'>
        ✓ License Renewal Approved
    </div>
</div>
<p style='font-size:15px;color:#e2e8f0;'>Hello <strong>{safeOwner}</strong>,</p>
<p style='color:#cbd5e1;line-height:1.6;'>
    Great news! Your license renewal request for <strong>{safeShop}</strong> has been officially approved by the platform administrator. Your shop's subscription is now active and extended.
</p>

<table style='width:100%;border-collapse:collapse;margin:20px 0;background-color:#0f172a;border-radius:8px;overflow:hidden;border:1px solid #334155;'>
    <tr style='border-bottom:1px solid #334155;'>
        <td style='padding:12px 16px;color:#94a3b8;font-size:13px;font-weight:600;'>Shop Name</td>
        <td style='padding:12px 16px;color:#f8fafc;font-weight:700;font-size:14px;'>{safeShop}</td>
    </tr>
    <tr style='border-bottom:1px solid #334155;'>
        <td style='padding:12px 16px;color:#94a3b8;font-size:13px;font-weight:600;'>Package / Plan</td>
        <td style='padding:12px 16px;color:#38bdf8;font-weight:700;font-size:14px;'>{safePackage}</td>
    </tr>
    <tr style='border-bottom:1px solid #334155;'>
        <td style='padding:12px 16px;color:#94a3b8;font-size:13px;font-weight:600;'>Extension Period</td>
        <td style='padding:12px 16px;color:#10b981;font-weight:700;font-size:14px;'>+{extensionDays} Days</td>
    </tr>
    <tr style='border-bottom:1px solid #334155;'>
        <td style='padding:12px 16px;color:#94a3b8;font-size:13px;font-weight:600;'>New Expiry (UTC)</td>
        <td style='padding:12px 16px;color:#f8fafc;font-family:monospace;font-size:13px;'>{newExpiryDate:dd MMM yyyy, HH:mm:ss} UTC</td>
    </tr>
    <tr>
        <td style='padding:12px 16px;color:#94a3b8;font-size:13px;font-weight:600;'>Local Time (IST)</td>
        <td style='padding:12px 16px;color:#fbbf24;font-family:monospace;font-size:13px;'>{istExpiry:dd MMM yyyy, hh:mm tt IST}</td>
    </tr>
</table>

{notesHtml}

<div style='text-align:center;margin:30px 0;'>
    <a href='#' class='btn' style='display:inline-block;background-color:#10b981;color:#ffffff!important;padding:12px 28px;border-radius:6px;text-decoration:none;font-weight:700;font-size:15px;'>
        Log In to SIMS Dashboard
    </a>
</div>

<p style='color:#94a3b8;font-size:13px;line-height:1.5;margin-top:25px;'>
    Thank you for choosing SIMS for your mobile shop inventory management. If you have any questions or require custom assistance, reply directly to this email or contact support.
</p>";

            await SendEmailAsync(toEmail, $"[SIMS] License Renewal Approved - {safeShop}", GetStandardHtmlTemplate("License Renewal Approved", content));
        }

        public async Task SendLicenseRenewalRejectedEmailAsync(
            string toEmail, 
            string shopName, 
            string ownerName, 
            string packageName, 
            string? rejectionReason)
        {
            var safeShop = System.Net.WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(shopName) ? "Your Shop" : shopName);
            var safeOwner = System.Net.WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(ownerName) ? "Store Owner" : ownerName);
            var safePackage = System.Net.WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(packageName) ? "Standard Package" : packageName);
            var safeReason = !string.IsNullOrWhiteSpace(rejectionReason) 
                ? System.Net.WebUtility.HtmlEncode(rejectionReason) 
                : "Your license renewal request could not be approved at this time. Please contact SaaS support or submit a new request with updated details.";

            var content = $@"
<div style='text-align:center;margin-bottom:20px;'>
    <div style='display:inline-block;background-color:rgba(239,68,68,0.15);color:#ef4444;padding:8px 18px;border-radius:20px;font-weight:700;font-size:14px;border:1px solid rgba(239,68,68,0.3);'>
        ✕ Renewal Request Update
    </div>
</div>
<p style='font-size:15px;color:#e2e8f0;'>Hello <strong>{safeOwner}</strong>,</p>
<p style='color:#cbd5e1;line-height:1.6;'>
    Thank you for reaching out to us. We have reviewed the license renewal request for <strong>{safeShop}</strong> regarding the package <strong>{safePackage}</strong>.
</p>

<div style='background-color:#1e293b;border-left:4px solid #ef4444;padding:16px;border-radius:4px;margin:20px 0;'>
    <strong style='color:#ef4444;font-size:13px;display:block;margin-bottom:6px;'>Reason for Decision / Administrator Notes:</strong>
    <p style='color:#f8fafc;font-size:14px;margin:0;line-height:1.6;font-family:sans-serif;'>{safeReason}</p>
</div>

<p style='color:#cbd5e1;line-height:1.6;'>
    If you have any questions, need clarification, or would like to discuss alternative subscription plans or payment verification, you may submit a revised renewal request from your dashboard or reach out to our support team.
</p>

<div style='text-align:center;margin:30px 0;'>
    <a href='#' class='btn' style='display:inline-block;background-color:#0284c7;color:#ffffff!important;padding:12px 28px;border-radius:6px;text-decoration:none;font-weight:700;font-size:15px;'>
        View Subscription Plans
    </a>
</div>

<p style='color:#94a3b8;font-size:13px;line-height:1.5;margin-top:25px;'>
    Smart Inventory Management System (SIMS) Team
</p>";

            await SendEmailAsync(toEmail, $"[SIMS] License Renewal Request Update - {safeShop}", GetStandardHtmlTemplate("License Renewal Request Update", content));
        }

        public async Task SendShopSuspendedEmailAsync(string toEmail, string shopName, string ownerName, string? reason = null)
        {
            var safeShop = System.Net.WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(shopName) ? "Your Shop" : shopName);
            var safeOwner = System.Net.WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(ownerName) ? "Store Owner" : ownerName);
            var safeReason = !string.IsNullOrWhiteSpace(reason)
                ? System.Net.WebUtility.HtmlEncode(reason)
                : "Your shop account has been placed on hold by the SaaS Platform Administrator. Operational modules, staff portals, and vendor access are temporarily paused.";

            var content = $@"
<div style='text-align:center;margin-bottom:20px;'>
    <div style='display:inline-block;background-color:rgba(239,68,68,0.15);color:#ef4444;padding:8px 18px;border-radius:20px;font-weight:700;font-size:14px;border:1px solid rgba(239,68,68,0.3);'>
        ⚠ Shop Account Suspended
    </div>
</div>
<p style='font-size:15px;color:#e2e8f0;'>Hello <strong>{safeOwner}</strong>,</p>
<p style='color:#cbd5e1;line-height:1.6;'>
    This is an important notification regarding your shop <strong>{safeShop}</strong> on the Smart Inventory Management System (SIMS). Your shop dashboard access has been suspended.
</p>

<div style='background-color:#1e293b;border-left:4px solid #ef4444;padding:16px;border-radius:4px;margin:20px 0;'>
    <strong style='color:#ef4444;font-size:13px;display:block;margin-bottom:6px;'>Reason / Notice:</strong>
    <p style='color:#f8fafc;font-size:14px;margin:0;line-height:1.6;'>{safeReason}</p>
</div>

<p style='color:#cbd5e1;line-height:1.6;'>
    While suspended, staff members, store employees, and associated suppliers will not be able to process sales, perform inventory updates, or view internal store records.
</p>
<p style='color:#cbd5e1;line-height:1.6;'>
    To reactivate your shop account and resume full access, please contact our Platform Support Team immediately.
</p>

<p style='color:#94a3b8;font-size:13px;line-height:1.5;margin-top:25px;'>
    SIMS SaaS Platform Administrator
</p>";

            await SendEmailAsync(toEmail, $"[SIMS] Urgent: Shop Account Suspended - {safeShop}", GetStandardHtmlTemplate("Shop Account Suspended", content));
        }

        public async Task SendLicenseExpiredEmailAsync(string toEmail, string shopName, string ownerName, DateTime expiryDate)
        {
            var safeShop = System.Net.WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(shopName) ? "Your Shop" : shopName);
            var safeOwner = System.Net.WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(ownerName) ? "Store Owner" : ownerName);
            var istExpiry = expiryDate.AddMinutes(330).ToString("dd-MMM-yyyy, hh:mm tt IST");

            var content = $@"
<div style='text-align:center;margin-bottom:20px;'>
    <div style='display:inline-block;background-color:rgba(245,158,11,0.15);color:#f59e0b;padding:8px 18px;border-radius:20px;font-weight:700;font-size:14px;border:1px solid rgba(245,158,11,0.3);'>
        ⌛ Subscription License Expired
    </div>
</div>
<p style='font-size:15px;color:#e2e8f0;'>Hello <strong>{safeOwner}</strong>,</p>
<p style='color:#cbd5e1;line-height:1.6;'>
    The active subscription license for <strong>{safeShop}</strong> expired on <strong style='color:#f59e0b;'>{istExpiry}</strong>.
</p>

<div style='background-color:#1e293b;border-left:4px solid #f59e0b;padding:16px;border-radius:4px;margin:20px 0;'>
    <strong style='color:#f59e0b;font-size:13px;display:block;margin-bottom:6px;'>Service Impact:</strong>
    <p style='color:#f8fafc;font-size:14px;margin:0;line-height:1.6;'>
        Store sales, product editing, stock transfers, and invoice creation are temporarily paused until your subscription is renewed or extended. You can still log in to view and submit a license renewal request.
    </p>
</div>

<p style='color:#cbd5e1;line-height:1.6;'>
    Please renew your subscription plan immediately to retain continuous access to all your shop features and data.
</p>

<div style='text-align:center;margin:30px 0;'>
    <a href='#' class='btn' style='display:inline-block;background-color:#f59e0b;color:#111827!important;padding:12px 28px;border-radius:6px;text-decoration:none;font-weight:700;font-size:15px;'>
        Renew License Now
    </a>
</div>

<p style='color:#94a3b8;font-size:13px;line-height:1.5;margin-top:25px;'>
    Smart Inventory Management System (SIMS) Team
</p>";

            await SendEmailAsync(toEmail, $"[SIMS] Subscription License Expired - {safeShop}", GetStandardHtmlTemplate("Subscription License Expired", content));
        }
    }
}
