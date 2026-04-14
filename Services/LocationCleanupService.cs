using APM.StaffZen.API.Data;
using Microsoft.EntityFrameworkCore;

namespace APM.StaffZen.API.Services
{
    /// <summary>
    /// Background service that deletes EmployeeLocation records older than 90 days.
    /// Runs once every 24 hours at startup offset.
    /// This matches Jibble's policy: "Live location data is stored for up to 90 days
    /// before being automatically deleted."
    /// </summary>
    public class LocationCleanupService : BackgroundService
    {
        private readonly IServiceProvider _services;
        private readonly ILogger<LocationCleanupService> _logger;
        private static readonly TimeSpan Interval = TimeSpan.FromHours(24);
        private const int RetentionDays = 90;

        public LocationCleanupService(IServiceProvider services, ILogger<LocationCleanupService> logger)
        {
            _services = services;
            _logger   = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("LocationCleanupService started. Will purge GPS points older than {Days} days every 24 h.", RetentionDays);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(Interval, stoppingToken);

                    using var scope  = _services.CreateScope();
                    var       db     = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                    var       cutoff = DateTime.UtcNow.AddDays(-RetentionDays);

                    int deleted = await db.EmployeeLocations
                        .Where(l => l.RecordedAt < cutoff)
                        .ExecuteDeleteAsync(stoppingToken);

                    if (deleted > 0)
                        _logger.LogInformation("LocationCleanup: deleted {Count} GPS points older than {Date:yyyy-MM-dd}.",
                            deleted, cutoff);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "LocationCleanupService error — will retry in 24 h.");
                }
            }

            _logger.LogInformation("LocationCleanupService stopped.");
        }
    }
}
