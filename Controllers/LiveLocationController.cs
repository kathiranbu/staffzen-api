using APM.StaffZen.API.Data;
using APM.StaffZen.API.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace APM.StaffZen.API.Controllers
{
    /// <summary>
    /// Handles real-time GPS location tracking for clocked-in employees.
    ///
    /// Endpoints
    /// ─────────
    /// POST api/LiveLocation/update              → receive a GPS ping from a clocked-in employee
    /// GET  api/LiveLocation/current             → latest location of every clocked-in employee
    /// GET  api/LiveLocation/routes/{empId}      → all GPS points for an employee on a given date
    ///                                             (used to draw the Routes polyline)
    /// POST api/LiveLocation/geofence-check      → check if a GPS point is inside any geofence
    ///                                             (used for auto clock-in/clock-out)
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class LiveLocationController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<LiveLocationController> _logger;

        // Minimum metres the employee must move before we bother storing a new point.
        // Reduces duplicate points when the person is stationary.
        private const double MinMovementMetres = 10.0;

        public LiveLocationController(ApplicationDbContext context, ILogger<LiveLocationController> logger)
        {
            _context = context;
            _logger  = logger;
        }

        // ── POST api/LiveLocation/update ──────────────────────────────────────
        // Called by the browser's navigator.geolocation watchPosition (every ~60 s).
        // Only stores the point if the employee is currently clocked in.
        // Silently ignores duplicate/stationary pings within MinMovementMetres.
        [HttpPost("update")]
        public async Task<IActionResult> UpdateLocation([FromBody] LocationUpdateRequest req)
        {
            try
            {
                if (req.EmployeeId <= 0)
                    return BadRequest(new { error = "EmployeeId is required." });

                // Gate: only store location if the employee has an active live session
                var isClockedIn = await _context.TimeEntries.AnyAsync(t =>
                    t.EmployeeId == req.EmployeeId &&
                    t.ClockOut   == null           &&
                    !t.IsManual                    &&
                    !t.IsHourEntry);

                if (!isClockedIn)
                    return Ok(new { stored = false, reason = "not_clocked_in" });

                // Duplicate/stationary filter — skip if too close to last stored point
                var last = await _context.EmployeeLocations
                    .Where(l => l.EmployeeId == req.EmployeeId)
                    .OrderByDescending(l => l.RecordedAt)
                    .FirstOrDefaultAsync();

                if (last != null)
                {
                    double dist = HaversineMetres(req.Latitude, req.Longitude, last.Latitude, last.Longitude);
                    if (dist < MinMovementMetres)
                        return Ok(new { stored = false, reason = "no_significant_movement" });
                }

                var point = new EmployeeLocation
                {
                    EmployeeId = req.EmployeeId,
                    Latitude   = req.Latitude,
                    Longitude  = req.Longitude,
                    RecordedAt = DateTime.UtcNow,
                    Accuracy   = req.Accuracy,
                    Speed      = req.Speed,
                };
                _context.EmployeeLocations.Add(point);
                await _context.SaveChangesAsync();

                // ── Geofence auto clock-out check ──────────────────────────────
                // (Auto clock-in is not applicable here because the employee is already in.)
                // If geofencing is enabled and the employee has moved OUTSIDE all geofences
                // they were previously inside, we can trigger an auto clock-out.
                // This is optional and can be wired up later once geofence settings are stored.

                return Ok(new { stored = true, pointId = point.Id });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "UpdateLocation failed for employee {Id}", req.EmployeeId);
                return StatusCode(500, new { error = ex.Message });
            }
        }

        // ── GET api/LiveLocation/current ──────────────────────────────────────
        // Returns the most recent GPS point for every currently clocked-in employee.
        // Called by the LiveLocations.razor page on load and on Refresh.
        [HttpGet("current")]
        public async Task<IActionResult> GetCurrentLocations()
        {
            try
            {
                // All employees with an active (open, non-manual) time entry
                var clockedInEmpIds = await _context.TimeEntries
                    .Where(t => t.ClockOut == null && !t.IsManual && !t.IsHourEntry)
                    .Select(t => t.EmployeeId)
                    .Distinct()
                    .ToListAsync();

                if (!clockedInEmpIds.Any())
                    return Ok(new List<object>());

                // Employee details
                var employees = await _context.Employees
                    .Where(e => clockedInEmpIds.Contains(e.Id) && e.IsActive)
                    .Select(e => new { e.Id, e.FullName })
                    .ToListAsync();

                // For each employee, get their latest GPS point
                var result = new List<object>();
                foreach (var emp in employees)
                {
                    var loc = await _context.EmployeeLocations
                        .Where(l => l.EmployeeId == emp.Id)
                        .OrderByDescending(l => l.RecordedAt)
                        .FirstOrDefaultAsync();

                    result.Add(new
                    {
                        employeeId  = emp.Id,
                        fullName    = emp.FullName,
                        latitude    = loc?.Latitude,
                        longitude   = loc?.Longitude,
                        recordedAt  = loc?.RecordedAt,
                        accuracy    = loc?.Accuracy,
                        hasLocation = loc != null,
                    });
                }

                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetCurrentLocations failed");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        // ── GET api/LiveLocation/routes/{employeeId}?date=2026-03-23 ──────────
        // Returns all GPS points for an employee on a given calendar day (UTC date).
        // Used by the Routes sidebar to draw the polyline path on the map.
        [HttpGet("routes/{employeeId}")]
        public async Task<IActionResult> GetRoutes(int employeeId, [FromQuery] string date)
        {
            try
            {
                if (!DateTime.TryParse(date, out var parsedDate))
                    return BadRequest(new { error = "Invalid date. Use yyyy-MM-dd." });

                // Query in UTC — RecordedAt is stored as UTC
                var dayStart = DateTime.SpecifyKind(parsedDate.Date, DateTimeKind.Utc);
                var dayEnd   = dayStart.AddDays(1);

                var points = await _context.EmployeeLocations
                    .Where(l => l.EmployeeId == employeeId &&
                                l.RecordedAt >= dayStart   &&
                                l.RecordedAt <  dayEnd)
                    .OrderBy(l => l.RecordedAt)
                    .Select(l => new
                    {
                        lat        = l.Latitude,
                        lng        = l.Longitude,
                        recordedAt = l.RecordedAt,
                        accuracy   = l.Accuracy,
                    })
                    .ToListAsync();

                return Ok(points);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetRoutes failed for employee {Id}", employeeId);
                return StatusCode(500, new { error = ex.Message });
            }
        }

        // ── POST api/LiveLocation/geofence-check ──────────────────────────────
        // Checks whether a given GPS coordinate is inside any active geofence.
        // Returns the list of geofences the point is inside (may be empty).
        // Used by the browser before clock-in to enforce geofence restrictions.
        [HttpPost("geofence-check")]
        public async Task<IActionResult> GeofenceCheck([FromBody] GeofenceCheckRequest req)
        {
            try
            {
                var activeLocations = await _context.Locations
                    .Where(l => !l.IsArchived && l.RadiusMetres >= 300)
                    .Select(l => new { l.Id, l.Name, l.Latitude, l.Longitude, l.RadiusMetres })
                    .ToListAsync();

                var inside = activeLocations
                    .Where(loc => HaversineMetres(req.Latitude, req.Longitude,
                                                  loc.Latitude, loc.Longitude) <= loc.RadiusMetres)
                    .Select(loc => new
                    {
                        locationId   = loc.Id,
                        locationName = loc.Name,
                        distanceMetres = (int)HaversineMetres(req.Latitude, req.Longitude,
                                                               loc.Latitude, loc.Longitude),
                        radiusMetres = loc.RadiusMetres,
                    })
                    .ToList();

                return Ok(new
                {
                    isInsideGeofence = inside.Any(),
                    matchedLocations = inside,
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GeofenceCheck failed");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        // ── Haversine formula ─────────────────────────────────────────────────
        // Calculates the real-world distance in metres between two GPS coordinates.
        private static double HaversineMetres(double lat1, double lon1, double lat2, double lon2)
        {
            const double R    = 6_371_000; // Earth radius in metres
            double       dLat = (lat2 - lat1) * Math.PI / 180.0;
            double       dLon = (lon2 - lon1) * Math.PI / 180.0;
            double       a    = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
                              + Math.Cos(lat1 * Math.PI / 180.0)
                              * Math.Cos(lat2 * Math.PI / 180.0)
                              * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
            return R * 2.0 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1.0 - a));
        }
    }

    // ── Request DTOs ──────────────────────────────────────────────────────────

    public class LocationUpdateRequest
    {
        public int    EmployeeId { get; set; }
        public double Latitude   { get; set; }
        public double Longitude  { get; set; }
        /// <summary>GPS accuracy radius in metres as reported by the browser.</summary>
        public float? Accuracy   { get; set; }
        /// <summary>Movement speed in metres/second as reported by the browser.</summary>
        public float? Speed      { get; set; }
    }

    public class GeofenceCheckRequest
    {
        public double Latitude  { get; set; }
        public double Longitude { get; set; }
    }
}
