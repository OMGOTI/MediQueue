using MediQueue.Hubs;
using MediQueue.Services;
using Microsoft.AspNetCore.Authentication.Cookies;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();
builder.Services.AddSignalR();

// Data is kept in App_Data/mediqueue.json (created on first run)
builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddSingleton<IDataStore>(_ =>
    new JsonDataStore(Path.Combine(builder.Environment.ContentRootPath, "App_Data", "mediqueue.json")));
builder.Services.AddSingleton<IQueueNotifier, SignalRNotifier>();
builder.Services.AddSingleton<QueueService>();
builder.Services.AddHostedService<HousekeepingService>();

builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login";
        options.AccessDeniedPath = "/Account/AccessDenied";
        options.Cookie.Name = "mediqueue.auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
    });
builder.Services.AddAuthorization();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
}

app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapHub<QueueHub>("/hubs/queue");

// short link used on the token slip and the QR code:  /t/abc123xyz9
app.MapControllerRoute(name: "status", pattern: "t/{publicId}", defaults: new { controller = "Token", action = "Status" });
app.MapControllerRoute(name: "default", pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();

public partial class Program { }
