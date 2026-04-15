using Microsoft.EntityFrameworkCore;
using APM.StaffZen.API.Data;
using APM.StaffZen.API.Models;
using APM.StaffZen.API.Services;

// Fix DateTime issue with PostgreSQL (temporary safe fix)
AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

var builder = WebApplication.CreateBuilder(args);

// Add services
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// ✅ FIXED DbContext (clean and correct)
builder.Services.AddDbContext<ApplicationDbContext>(options =>
{
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection"));
});

// Email + Services
builder.Services.Configure<EmailSettings>(builder.Configuration.GetSection("EmailSettings"));
builder.Services.AddScoped<EmailService>();
builder.Services.AddScoped<PasswordService>();
builder.Services.AddScoped<FirebaseService>();
builder.Services.AddHttpClient();
builder.Services.AddHttpContextAccessor();

// Background services
builder.Services.AddHostedService<LocationCleanupService>();
builder.Services.AddHostedService<AutoClockOutService>();

// CORS
var blazorOrigin = "https://staffzen-app.onrender.com";
builder.Services.AddCors(options =>
{
    options.AddPolicy("BlazorPolicy", policy =>
        policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod());
});

var app = builder.Build();

// Auto-migrate database
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var log = scope.ServiceProvider.GetRequiredService<ILogger<ApplicationDbContext>>();

    db.Database.Migrate();
}

// Ensure uploads folder exists
var uploadsPath = Path.Combine(
    app.Environment.WebRootPath ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot"),
    "uploads"
);

try
{
    Directory.CreateDirectory(uploadsPath);
}
catch (Exception ex)
{
    var startupLog = app.Services.GetRequiredService<ILogger<Program>>();
    startupLog.LogError(ex, "Could not create wwwroot/uploads. Path: {Path}", uploadsPath);
}

// Middleware
app.UseSwagger();
app.UseSwaggerUI();

app.UseStaticFiles();
app.UseHttpsRedirection();
app.UseCors("BlazorPolicy");
app.UseAuthorization();

app.MapControllers();

app.Run();