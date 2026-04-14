using Microsoft.EntityFrameworkCore;
using APM.StaffZen.API.Data;
using APM.StaffZen.API.Models;
using APM.StaffZen.API.Services;

// All schema changes are handled by EF Core migrations (Migrations/ folder).
// No manual SQL needed. Just run the app — db.Database.Migrate() runs on startup.

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.Configure<EmailSettings>(builder.Configuration.GetSection("EmailSettings"));
builder.Services.AddScoped<EmailService>();
builder.Services.AddScoped<PasswordService>();
builder.Services.AddScoped<FirebaseService>();
builder.Services.AddHttpClient();
builder.Services.AddHttpContextAccessor();

// Background service: purges GPS location points older than 90 days
builder.Services.AddHostedService<LocationCleanupService>();

// Background service: automatically clocks out all open sessions at the configured time
builder.Services.AddHostedService<AutoClockOutService>();

var blazorOrigin = builder.Configuration["AppSettings:BlazorBaseUrl"] ?? "https://localhost:7299";
builder.Services.AddCors(options =>
{
    options.AddPolicy("BlazorPolicy", policy =>
        // AllowAnyOrigin lets the mobile phone (on your LAN) reach the API.
        // In production, replace with your real domain using .WithOrigins("https://yourdomain.com")
        policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod());
});

var app = builder.Build();

// Auto-migrate on startup — all schema changes live in Migrations/.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var log = scope.ServiceProvider.GetRequiredService<ILogger<ApplicationDbContext>>();

    // Apply any pending migrations (idempotent).
    db.Database.Migrate();
}
    // Safety net: if the LeaveRequests migration was previously recorded as applied
    // via raw SQL but the table was never actually created, create it now.
  
// ── Auto-create wwwroot/uploads so selfie photos always have a place to land ──
// This runs every startup — Directory.CreateDirectory is a no-op if it already exists.
var uploadsPath = Path.Combine(app.Environment.WebRootPath ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot"), "uploads");
try
{
    Directory.CreateDirectory(uploadsPath);
}
catch (Exception ex)
{
    var startupLog = app.Services.GetRequiredService<ILogger<Program>>();
    startupLog.LogError(ex, "Could not create wwwroot/uploads — selfie photos will not be saved. Path: {Path}", uploadsPath);
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseStaticFiles();
app.UseHttpsRedirection();
app.UseCors("BlazorPolicy");
app.UseAuthorization();
app.MapControllers();

app.Run();