using DotNetEnv;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using InventoryManagementSystem.Configuration;
using InventoryManagementSystem.Data;
using InventoryManagementSystem.Interfaces;
using InventoryManagementSystem.Repositories;
using InventoryManagementSystem.Services;
using InventoryManagementSystem.Middleware;
using InventoryManagementSystem.Extensions;
using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

// Load .env automatically if present (supports local development environments)
try
{
    Env.TraversePath().Load();
}
catch (Exception ex)
{
    Console.WriteLine($"[Config] DotNetEnv load notice: {ex.Message}");
}

var builder = WebApplication.CreateBuilder(args);

// Configure Logging Filter to reduce noisy Debug/Information chatter
builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);
builder.Logging.AddFilter("Microsoft.Hosting.Lifetime", LogLevel.Information);

// Configure resolution order: Environment Variables -> User Secrets -> appsettings.Development.json -> appsettings.json
builder.Configuration
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: true);

if (builder.Environment.IsDevelopment())
{
    builder.Configuration.AddUserSecrets<Program>(optional: true);
}

builder.Configuration.AddEnvironmentVariables();

// Configure Forwarded Headers for Render SSL/TLS reverse proxy
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

// Configure Response Compression for production performance
builder.Services.AddResponseCompression();

// Helper method to resolve settings with fallback priority
string GetConfigValue(string envKey, string configKey, string defaultValue = "")
{
    var val = Environment.GetEnvironmentVariable(envKey);
    if (!string.IsNullOrWhiteSpace(val)) return val.Trim();

    val = builder.Configuration[configKey];
    if (!string.IsNullOrWhiteSpace(val)) return val.Trim();

    val = builder.Configuration[envKey];
    if (!string.IsNullOrWhiteSpace(val)) return val.Trim();

    return defaultValue;
}

// Resolve strongly-typed setting objects
var mongoSettings = new MongoDbSettings
{
    ConnectionString = GetConfigValue("MONGODB_CONNECTION_STRING", "MongoDbSettings:ConnectionString"),
    DatabaseName = GetConfigValue("MONGODB_DATABASE", "MongoDbSettings:DatabaseName", "SIMS_Db")
};

var cloudinarySettings = new CloudinarySettings
{
    CloudName = GetConfigValue("CLOUDINARY_CLOUD_NAME", "CloudinarySettings:CloudName"),
    ApiKey = GetConfigValue("CLOUDINARY_API_KEY", "CloudinarySettings:ApiKey"),
    ApiSecret = GetConfigValue("CLOUDINARY_API_SECRET", "CloudinarySettings:ApiSecret")
};

int.TryParse(GetConfigValue("BREVO_PORT", "BrevoSettings:Port", "587"), out int brevoPort);
var brevoSettings = new BrevoSettings
{
    Host = GetConfigValue("BREVO_HOST", "BrevoSettings:Host", "smtp-relay.brevo.com"),
    Port = brevoPort > 0 ? brevoPort : 587,
    Username = GetConfigValue("BREVO_USERNAME", "BrevoSettings:Username"),
    Password = GetConfigValue("BREVO_PASSWORD", "BrevoSettings:Password"),
    FromEmail = GetConfigValue("BREVO_FROM_EMAIL", "BrevoSettings:FromEmail"),
    FromName = GetConfigValue("BREVO_FROM_NAME", "BrevoSettings:FromName", "SIMS System")
};

// Register strongly-typed IOptions<T> instances
builder.Services.Configure<MongoDbSettings>(options =>
{
    options.ConnectionString = mongoSettings.ConnectionString;
    options.DatabaseName = mongoSettings.DatabaseName;
});

builder.Services.Configure<CloudinarySettings>(options =>
{
    options.CloudName = cloudinarySettings.CloudName;
    options.ApiKey = cloudinarySettings.ApiKey;
    options.ApiSecret = cloudinarySettings.ApiSecret;
});

builder.Services.Configure<BrevoSettings>(options =>
{
    options.Host = brevoSettings.Host;
    options.Port = brevoSettings.Port;
    options.Username = brevoSettings.Username;
    options.Password = brevoSettings.Password;
    options.FromEmail = brevoSettings.FromEmail;
    options.FromName = brevoSettings.FromName;
});

var brevoApiKey = GetConfigValue("BREVO_API_KEY", "BrevoApiSettings:ApiKey");
builder.Services.Configure<BrevoApiSettings>(options =>
{
    options.ApiKey = brevoApiKey;
    options.SenderEmail = !string.IsNullOrWhiteSpace(brevoSettings.FromEmail) ? brevoSettings.FromEmail : "noreply@sims.com";
    options.SenderName = !string.IsNullOrWhiteSpace(brevoSettings.FromName) ? brevoSettings.FromName : "SIMS System";
});

builder.Services.Configure<AppSettings>(builder.Configuration.GetSection("AppSettings"));

// Add services to the container
builder.Services.AddMemoryCache();
builder.Services.AddHttpClient();
builder.Services.AddControllersWithViews(options =>
{
    options.Filters.Add<InventoryManagementSystem.Filters.PermissionAuthorizeFilter>();
});

// Register Singleton MongoClient for connection pooling
builder.Services.AddSingleton<IMongoClient>(sp =>
{
    var settings = MongoClientSettings.FromConnectionString(mongoSettings.ConnectionString);
    settings.ServerSelectionTimeout = TimeSpan.FromSeconds(15);
    return new MongoClient(settings);
});
builder.Services.AddSingleton<MongoDbContext>();

// Register HttpContextAccessor
builder.Services.AddHttpContextAccessor();

// Register Repositories
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IAuditLogRepository, AuditLogRepository>();
builder.Services.AddScoped<INotificationRepository, NotificationRepository>();
builder.Services.AddScoped<ICategoryRepository, CategoryRepository>();
builder.Services.AddScoped<IProductRepository, ProductRepository>();
builder.Services.AddScoped<IStockTransactionRepository, StockTransactionRepository>();
builder.Services.AddScoped<ISaleRepository, SaleRepository>();
builder.Services.AddScoped<IInventoryAlertRepository, InventoryAlertRepository>();
builder.Services.AddScoped<IDeviceRepository, DeviceRepository>();
builder.Services.AddScoped<ICustomerRepository, CustomerRepository>();
builder.Services.AddScoped<ISupplierRepository, SupplierRepository>();
builder.Services.AddScoped<IReturnRepository, ReturnRepository>();
builder.Services.AddScoped<IExchangeRepository, ExchangeRepository>();
builder.Services.AddScoped<IRepairRepository, RepairRepository>();
builder.Services.AddScoped<ISupplierOrderRepository, SupplierOrderRepository>();
builder.Services.AddScoped<ISupplierPurchaseReturnRepository, SupplierPurchaseReturnRepository>();

// Register Services
builder.Services.AddSingleton<IPermissionDiscoveryService, PermissionDiscoveryService>();
builder.Services.AddScoped<IPermissionService, PermissionService>();
builder.Services.AddScoped<IAccountValidationService, AccountValidationService>();
builder.Services.AddHttpClient<IBrevoEmailService, BrevoEmailService>();
builder.Services.AddScoped<IInventoryAlertService, InventoryAlertService>();
builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddScoped<IImageService, CloudinaryImageService>();
builder.Services.AddScoped<IAuditLogService, AuditLogService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<ICategoryService, CategoryService>();
builder.Services.AddScoped<IProductService, ProductService>();
builder.Services.AddScoped<IStockService, StockService>();
builder.Services.AddScoped<ISalesService, SalesService>();
builder.Services.AddScoped<IReportService, ReportService>();
builder.Services.AddScoped<IDeviceService, DeviceService>();
builder.Services.AddScoped<ICustomerService, CustomerService>();
builder.Services.AddScoped<ISupplierService, SupplierService>();
builder.Services.AddScoped<ISupplierOrderService, SupplierOrderService>();
builder.Services.AddScoped<ISupplierPurchaseReturnService, SupplierPurchaseReturnService>();
builder.Services.AddScoped<IReturnService, ReturnService>();
builder.Services.AddScoped<IExchangeService, ExchangeService>();
builder.Services.AddScoped<IRepairService, RepairService>();
builder.Services.AddSingleton<IManufacturerRegistryService, ManufacturerRegistryService>();
builder.Services.AddHttpClient<IMobileSpecSearchService, MobileSpecSearchService>();

// Configure Cookie Authentication
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login";
        options.LogoutPath = "/Account/Logout";
        options.AccessDeniedPath = "/Account/AccessDenied";
        options.ExpireTimeSpan = TimeSpan.FromMinutes(20);
        options.SlidingExpiration = true;
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    });

// Configure Session
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(20);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

// Dynamic Port Assignment (Supports Render PORT env var & Local Dev Fallback)
var portEnv = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrWhiteSpace(portEnv) && int.TryParse(portEnv, out int renderPort))
{
    Console.WriteLine($"[PORT CONFIG] Injected PORT environment variable detected: {renderPort}. Binding to http://0.0.0.0:{renderPort}");
    builder.WebHost.UseUrls($"http://0.0.0.0:{renderPort}");
}
else
{
    int targetPort = 5094;
    if (!IsPortAvailable(targetPort))
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"[PORT FALLBACK NOTICE] Port {targetPort} is occupied by another process. Automatically finding fallback port...");
        Console.ResetColor();

        for (int p = 5095; p <= 5105; p++)
        {
            if (IsPortAvailable(p))
            {
                targetPort = p;
                break;
            }
        }
    }

    builder.WebHost.UseUrls($"http://0.0.0.0:{targetPort}");
}

var app = builder.Build();

// Startup Configuration Validation
if (string.IsNullOrWhiteSpace(mongoSettings.ConnectionString))
{
    app.Logger.LogError("MongoDB connection string not configured.");
    throw new InvalidOperationException("MongoDB connection string not configured.");
}

if (string.IsNullOrWhiteSpace(cloudinarySettings.CloudName) ||
    string.IsNullOrWhiteSpace(cloudinarySettings.ApiKey) ||
    string.IsNullOrWhiteSpace(cloudinarySettings.ApiSecret))
{
    app.Logger.LogWarning("Cloudinary configuration missing (Image upload functionality may be disabled).");
}

if (string.IsNullOrWhiteSpace(brevoSettings.Host) ||
    string.IsNullOrWhiteSpace(brevoSettings.Username) ||
    string.IsNullOrWhiteSpace(brevoSettings.Password) ||
    string.IsNullOrWhiteSpace(brevoSettings.FromEmail))
{
    app.Logger.LogWarning("Brevo SMTP configuration missing (Email notifications disabled).");
}

// Configure HTTP request pipeline & reverse proxy headers
app.UseForwardedHeaders();
app.UseResponseCompression();
app.UseMiddleware<ExceptionMiddleware>();

// Static Files Setup
var wwwrootPath = Path.Combine(builder.Environment.ContentRootPath, "wwwroot");
if (!Directory.Exists(wwwrootPath))
{
    wwwrootPath = Path.Combine(AppContext.BaseDirectory, "wwwroot");
}

if (Directory.Exists(wwwrootPath))
{
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new PhysicalFileProvider(wwwrootPath),
        RequestPath = ""
    });
}
else
{
    app.UseStaticFiles();
}

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseRouting();
app.UseSession();
app.UseAuthentication();
app.UseAuthorization();

// Lightweight Health Check Endpoint for Render Zero-Downtime Monitoring
app.MapGet("/health", () => Results.Ok(new
{
    status = "Healthy",
    service = "SIMS Enterprise Inventory System",
    timestamp = DateTime.UtcNow
}));

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

// Initialize MongoDB Indexes & Seed Database on Startup
try
{
    var mongoDbContext = app.Services.GetRequiredService<MongoDbContext>();
    await mongoDbContext.InitializeIndexesAsync();
    await app.SeedDatabaseAsync();
}
catch (Exception ex)
{
    app.Logger.LogWarning("Database index initialization or seeding notice: {Message}", ex.Message);
}

app.Run();

// Helper method to check if a TCP port is available for binding on 0.0.0.0
static bool IsPortAvailable(int port)
{
    try
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        socket.Bind(new IPEndPoint(IPAddress.Any, port));
        return true;
    }
    catch
    {
        return false;
    }
}
