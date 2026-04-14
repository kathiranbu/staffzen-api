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
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

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
    var db  = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var log = scope.ServiceProvider.GetRequiredService<ILogger<ApplicationDbContext>>();

    // Apply any pending migrations (idempotent).
    db.Database.Migrate();

    // Safety net: if the LeaveRequests migration was previously recorded as applied
    // via raw SQL but the table was never actually created, create it now.
    try
    {
        db.Database.ExecuteSqlRaw(@"
            IF NOT EXISTS (
                SELECT 1 FROM INFORMATION_SCHEMA.TABLES
                WHERE TABLE_NAME = 'LeaveRequests'
            )
            BEGIN
                CREATE TABLE LeaveRequests (
                    Id           INT           IDENTITY(1,1) PRIMARY KEY,
                    EmployeeId   INT           NOT NULL,
                    StartDate    DATETIME2     NOT NULL,
                    EndDate      DATETIME2     NOT NULL,
                    Reason       NVARCHAR(MAX) NULL,
                    Status       NVARCHAR(MAX) NOT NULL DEFAULT N'Pending_TeamLead',
                    ReviewedBy   NVARCHAR(MAX) NULL,
                    RejectReason NVARCHAR(MAX) NULL,
                    CreatedAt    DATETIME2     NOT NULL DEFAULT GETUTCDATE(),
                    CONSTRAINT FK_LeaveRequests_Employees
                        FOREIGN KEY (EmployeeId)
                        REFERENCES Employees(Id)
                        ON DELETE CASCADE
                );
                CREATE INDEX IX_LeaveRequests_EmployeeId ON LeaveRequests(EmployeeId);
            END");
    }
    catch (Exception ex)
    {
        log.LogError(ex, "Safety-net table creation for LeaveRequests failed.");
    }

    // Safety net for TimeTrackingPolicies (added for AutoClockOut feature)
    try
    {
        db.Database.ExecuteSqlRaw(@"
            IF NOT EXISTS (
                SELECT 1 FROM INFORMATION_SCHEMA.TABLES
                WHERE TABLE_NAME = 'TimeTrackingPolicies'
            )
            BEGIN
                CREATE TABLE TimeTrackingPolicies (
                    Id                        INT           IDENTITY(1,1) PRIMARY KEY,
                    OrganizationId            INT           NOT NULL,
                    AutoClockOutEnabled       BIT           NOT NULL DEFAULT 0,
                    AutoClockOutAfterDuration BIT           NOT NULL DEFAULT 0,
                    AutoClockOutAfterHours    INT           NOT NULL DEFAULT 8,
                    AutoClockOutAfterMins     INT           NOT NULL DEFAULT 0,
                    AutoClockOutAtTime        BIT           NOT NULL DEFAULT 0,
                    AutoClockOutTime          NVARCHAR(5)   NOT NULL DEFAULT N'23:00',
                    CONSTRAINT FK_TimeTrackingPolicies_Organizations
                        FOREIGN KEY (OrganizationId)
                        REFERENCES Organizations(Id)
                        ON DELETE CASCADE
                );
                CREATE UNIQUE INDEX IX_TimeTrackingPolicies_OrganizationId
                    ON TimeTrackingPolicies(OrganizationId);
            END");
    }
    catch (Exception ex)
    {
        log.LogError(ex, "Safety-net table creation for TimeTrackingPolicies failed.");
    }

    // ── Safety net: selfie columns on TimeEntries ──────────────────────────────
    // Migration 20260402000000_AddSelfieAndVerificationPolicy adds these, but if
    // the migration was never run the API silently drops selfie URLs. Guard here
    // so clock-in/clock-out selfies always get stored even on older databases.
    try
    {
        db.Database.ExecuteSqlRaw(@"
            IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
                           WHERE TABLE_NAME='TimeEntries' AND COLUMN_NAME='ClockInSelfieUrl')
                ALTER TABLE TimeEntries ADD ClockInSelfieUrl NVARCHAR(MAX) NULL;

            IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
                           WHERE TABLE_NAME='TimeEntries' AND COLUMN_NAME='ClockOutSelfieUrl')
                ALTER TABLE TimeEntries ADD ClockOutSelfieUrl NVARCHAR(MAX) NULL;");
    }
    catch (Exception ex)
    {
        log.LogError(ex, "Safety-net column creation for TimeEntries selfie URLs failed.");
    }

    // ── Safety net: WorkSchedules verification policy columns ──────────────────
    try
    {
        db.Database.ExecuteSqlRaw(@"
            IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
                           WHERE TABLE_NAME='WorkSchedules' AND COLUMN_NAME='RequireFaceVerification')
                ALTER TABLE WorkSchedules ADD RequireFaceVerification BIT NOT NULL DEFAULT 0;

            IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
                           WHERE TABLE_NAME='WorkSchedules' AND COLUMN_NAME='RequireSelfie')
                ALTER TABLE WorkSchedules ADD RequireSelfie BIT NOT NULL DEFAULT 0;

            IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
                           WHERE TABLE_NAME='WorkSchedules' AND COLUMN_NAME='UnusualBehavior')
                ALTER TABLE WorkSchedules ADD UnusualBehavior NVARCHAR(20) NOT NULL DEFAULT N'Blocked';");
    }
    catch (Exception ex)
    {
        log.LogError(ex, "Safety-net column creation for WorkSchedules verification policy failed.");
    }

    // ── Safety net: PayPeriodSettings table ────────────────────────────────────
    // Required by PayPeriodService (Blazor) and the pay-period API endpoints.
    try
    {
        db.Database.ExecuteSqlRaw(@"
            IF NOT EXISTS (
                SELECT 1 FROM INFORMATION_SCHEMA.TABLES
                WHERE TABLE_NAME = 'PayPeriodSettings'
            )
            BEGIN
                CREATE TABLE PayPeriodSettings (
                    Id             INT           IDENTITY(1,1) PRIMARY KEY,
                    OrganizationId INT           NOT NULL,
                    Name           NVARCHAR(200) NOT NULL DEFAULT N'Default',
                    Frequency      NVARCHAR(50)  NOT NULL DEFAULT N'Monthly',
                    StartDow       INT           NOT NULL DEFAULT 1,
                    FirstDay       INT           NOT NULL DEFAULT 1,
                    SemiDay        INT           NOT NULL DEFAULT 16,
                    StartDate      DATETIME2     NOT NULL DEFAULT GETUTCDATE(),
                    CONSTRAINT FK_PayPeriodSettings_Organizations
                        FOREIGN KEY (OrganizationId)
                        REFERENCES Organizations(Id)
                        ON DELETE CASCADE
                );
                CREATE UNIQUE INDEX IX_PayPeriodSettings_OrganizationId
                    ON PayPeriodSettings(OrganizationId);
            END");
    }
    catch (Exception ex)
    {
        log.LogError(ex, "Safety-net table creation for PayPeriodSettings failed.");
    }

    // ── Safety net: Notifications table ──────────────────────────────────────
    // Handles all schema mismatch scenarios without any manual migration command:
    //   1. Table does not exist → CREATE it in full.
    //   2. Table exists but missing columns (old schema) → ALTER to add them.
    //   3. Table exists with extra stale columns not in the model → DROP them.
    //      (e.g. OrganizationId was in an early dev version — causes NULL insert errors)
    //   4. Table already correct → all guards are no-ops.
    try
    {
        db.Database.ExecuteSqlRaw(@"
            -- 1. Create table if missing
            IF NOT EXISTS (
                SELECT 1 FROM INFORMATION_SCHEMA.TABLES
                WHERE TABLE_NAME = 'Notifications'
            )
            BEGIN
                CREATE TABLE Notifications (
                    Id          INT           IDENTITY(1,1) PRIMARY KEY,
                    RecipientId INT           NOT NULL,
                    Type        NVARCHAR(50)  NOT NULL DEFAULT N'',
                    Title       NVARCHAR(200) NOT NULL DEFAULT N'',
                    Message     NVARCHAR(500) NOT NULL DEFAULT N'',
                    ReferenceId INT           NOT NULL DEFAULT 0,
                    IsRead      BIT           NOT NULL DEFAULT 0,
                    CreatedAt   DATETIME2     NOT NULL DEFAULT GETUTCDATE(),
                    CONSTRAINT FK_Notifications_Employees_RecipientId
                        FOREIGN KEY (RecipientId)
                        REFERENCES Employees(Id)
                        ON DELETE CASCADE
                );
                CREATE INDEX IX_Notifications_RecipientId_CreatedAt
                    ON Notifications(RecipientId, CreatedAt);
            END");

        // 2. Add any missing columns in case the table was created with an older schema
        db.Database.ExecuteSqlRaw(@"
            IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
                           WHERE TABLE_NAME='Notifications' AND COLUMN_NAME='RecipientId')
                ALTER TABLE Notifications ADD RecipientId INT NOT NULL DEFAULT 0;

            IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
                           WHERE TABLE_NAME='Notifications' AND COLUMN_NAME='Type')
                ALTER TABLE Notifications ADD Type NVARCHAR(50) NOT NULL DEFAULT N'';

            IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
                           WHERE TABLE_NAME='Notifications' AND COLUMN_NAME='Title')
                ALTER TABLE Notifications ADD Title NVARCHAR(200) NOT NULL DEFAULT N'';

            IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
                           WHERE TABLE_NAME='Notifications' AND COLUMN_NAME='Message')
                ALTER TABLE Notifications ADD Message NVARCHAR(500) NOT NULL DEFAULT N'';

            IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
                           WHERE TABLE_NAME='Notifications' AND COLUMN_NAME='ReferenceId')
                ALTER TABLE Notifications ADD ReferenceId INT NOT NULL DEFAULT 0;

            IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
                           WHERE TABLE_NAME='Notifications' AND COLUMN_NAME='IsRead')
                ALTER TABLE Notifications ADD IsRead BIT NOT NULL DEFAULT 0;

            IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
                           WHERE TABLE_NAME='Notifications' AND COLUMN_NAME='CreatedAt')
                ALTER TABLE Notifications ADD CreatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE();");

        // 3. Drop stale columns not in the EF model — causes NULL insert errors if left.
        //    Must drop indexes first, then FKs, then the column itself.
        //    Runs inside a single EXEC block so variable scope is contained.
        db.Database.ExecuteSqlRaw(@"
            IF EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
                       WHERE TABLE_NAME='Notifications' AND COLUMN_NAME='OrganizationId')
            BEGIN
                -- Step A: drop every index on Notifications that covers OrganizationId
                DECLARE @idxSql NVARCHAR(MAX) = N'';
                SELECT @idxSql = @idxSql +
                    'DROP INDEX [' + i.name + '] ON [Notifications];' + CHAR(13)
                FROM sys.indexes i
                JOIN sys.index_columns ic ON i.object_id  = ic.object_id
                                          AND i.index_id  = ic.index_id
                JOIN sys.columns c        ON ic.object_id = c.object_id
                                          AND ic.column_id = c.column_id
                WHERE i.object_id        = OBJECT_ID('Notifications')
                  AND c.name             = 'OrganizationId'
                  AND i.is_primary_key   = 0;
                IF LEN(@idxSql) > 0 EXEC(@idxSql);

                -- Step B: drop every FK on Notifications that references OrganizationId
                DECLARE @fkSql NVARCHAR(MAX) = N'';
                SELECT @fkSql = @fkSql +
                    'ALTER TABLE [Notifications] DROP CONSTRAINT [' + fk.name + '];' + CHAR(13)
                FROM sys.foreign_keys fk
                JOIN sys.foreign_key_columns fkc ON fk.object_id      = fkc.constraint_object_id
                JOIN sys.columns c               ON fkc.parent_object_id = c.object_id
                                                AND fkc.parent_column_id = c.column_id
                WHERE fk.parent_object_id = OBJECT_ID('Notifications')
                  AND c.name              = 'OrganizationId';
                IF LEN(@fkSql) > 0 EXEC(@fkSql);

                -- Step C: now safe to drop the stale column
                ALTER TABLE Notifications DROP COLUMN OrganizationId;
            END");

        // 4. Add the composite index if missing (safe to skip if already there)
        db.Database.ExecuteSqlRaw(@"
            IF NOT EXISTS (
                SELECT 1 FROM sys.indexes
                WHERE name = 'IX_Notifications_RecipientId_CreatedAt'
                  AND object_id = OBJECT_ID('Notifications')
            )
                CREATE INDEX IX_Notifications_RecipientId_CreatedAt
                    ON Notifications(RecipientId, CreatedAt);");

        // Note: The FK constraint (FK_Notifications_Employees_RecipientId) is intentionally
        // NOT added here. db.Database.Migrate() above handles it via the migration when the
        // table is freshly created. Adding it in the safety-net risks a constraint conflict
        // error (SQL Error 547) if existing rows have orphaned RecipientId values.
    }
    catch (Exception ex)
    {
        log.LogError(ex, "Safety-net table/column creation for Notifications failed.");
    }
}

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