using APM.StaffZen.API.Data;
using APM.StaffZen.API.Dtos;
using APM.StaffZen.API.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace APM.StaffZen.API.Controllers
{
    /// <summary>
    /// Manages Locations and Geofences.
    ///
    /// Endpoints
    /// ─────────
    /// GET    api/Locations                → all active locations
    /// GET    api/Locations/archived       → archived locations
    /// GET    api/Locations/{id}           → single location
    /// POST   api/Locations                → add location (address-search)
    /// POST   api/Locations/missing        → ★ ADD MISSING LOCATION (pin-drag)
    /// PUT    api/Locations/{id}           → edit location
    /// PUT    api/Locations/bulk-radius    → bulk radius update
    /// DELETE api/Locations/{id}           → archive (soft-delete)
    /// DELETE api/Locations/{id}/permanent → permanently delete (archived only)
    /// POST   api/Locations/{id}/restore   → restore from archive
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class LocationsController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<LocationsController> _logger;
        private const int MinGeofenceRadius = 300;

        public LocationsController(ApplicationDbContext context, ILogger<LocationsController> logger)
        {
            _context = context;
            _logger  = logger;
        }

        // ── GET all active ─────────────────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            try
            {
                var list = await _context.Locations
                    .Where(l => !l.IsArchived)
                    .OrderBy(l => l.Name)
                    .Select(l => new
                    {
                        l.Id, l.Name, l.Latitude, l.Longitude,
                        l.Street, l.City, l.Country, l.PostalCode,
                        l.RadiusMetres, l.IsMissing,
                        geofenceReady = l.RadiusMetres >= MinGeofenceRadius
                    })
                    .ToListAsync();
                return Ok(list);
            }
            catch (Exception ex) { _logger.LogError(ex, "GetAll Locations failed"); return StatusCode(500, new { error = ex.Message }); }
        }

        // ── GET archived ───────────────────────────────────────────────────
        [HttpGet("archived")]
        public async Task<IActionResult> GetArchived()
        {
            try
            {
                var list = await _context.Locations.Where(l => l.IsArchived).OrderBy(l => l.Name).ToListAsync();
                return Ok(list);
            }
            catch (Exception ex) { _logger.LogError(ex, "GetArchived Locations failed"); return StatusCode(500, new { error = ex.Message }); }
        }

        // ── GET single ─────────────────────────────────────────────────────
        [HttpGet("{id}")]
        public async Task<IActionResult> Get(int id)
        {
            try
            {
                var loc = await _context.Locations.FindAsync(id);
                return loc == null ? NotFound(new { error = "Location not found." }) : Ok(loc);
            }
            catch (Exception ex) { _logger.LogError(ex, "Get Location {Id} failed", id); return StatusCode(500, new { error = ex.Message }); }
        }

        // ── POST add (normal address-search flow) ──────────────────────────
        [HttpPost]
        public async Task<IActionResult> Add([FromBody] AddLocationDto dto)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(dto.Name))
                    return BadRequest(new { error = "Location name is required." });

                var loc = MapFromDto(dto);
                _context.Locations.Add(loc);
                await _context.SaveChangesAsync();
                _logger.LogInformation("Location added: {Name} (id={Id})", loc.Name, loc.Id);
                return Ok(BuildResponse(loc));
            }
            catch (Exception ex) { _logger.LogError(ex, "Add Location failed"); return StatusCode(500, new { error = ex.Message }); }
        }

        // ── POST add missing location ★ (pin-drag flow) ────────────────────
        /// <summary>
        /// Called when the user clicks "+ Add missing location" in the Blazor UI,
        /// drags the pin on the map, fills in the details panel, then saves.
        /// IsMissing is forced to true regardless of the request body.
        /// Minimum radius is enforced to 300 m so geofence auto-clock works.
        /// </summary>
        [HttpPost("missing")]
        public async Task<IActionResult> AddMissing([FromBody] AddLocationDto dto)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(dto.Name))
                    return BadRequest(new { error = "Location name is required." });

                if (dto.Latitude == 0 && dto.Longitude == 0)
                    return BadRequest(new { error = "Valid coordinates are required. Drag the pin to your location on the map." });

                if (dto.Latitude  < -90  || dto.Latitude  > 90)
                    return BadRequest(new { error = "Latitude must be between -90 and 90." });

                if (dto.Longitude < -180 || dto.Longitude > 180)
                    return BadRequest(new { error = "Longitude must be between -180 and 180." });

                var loc = MapFromDto(dto);
                loc.IsMissing    = true;   // always forced for this endpoint
                loc.RadiusMetres = Math.Max(loc.RadiusMetres, MinGeofenceRadius);

                _context.Locations.Add(loc);
                await _context.SaveChangesAsync();

                _logger.LogInformation(
                    "Missing location added: {Name} @ ({Lat},{Lng}) r={R}m (id={Id})",
                    loc.Name, loc.Latitude, loc.Longitude, loc.RadiusMetres, loc.Id);

                return Ok(new
                {
                    message       = "Missing location added successfully.",
                    geofenceReady = loc.RadiusMetres >= MinGeofenceRadius,
                    location      = BuildResponse(loc)
                });
            }
            catch (Exception ex) { _logger.LogError(ex, "AddMissing Location failed"); return StatusCode(500, new { error = ex.Message }); }
        }

        // ── PUT update ─────────────────────────────────────────────────────
        [HttpPut("{id}")]
        public async Task<IActionResult> Update(int id, [FromBody] UpdateLocationDto dto)
        {
            try
            {
                var loc = await _context.Locations.FindAsync(id);
                if (loc == null) return NotFound(new { error = "Location not found." });
                if (string.IsNullOrWhiteSpace(dto.Name)) return BadRequest(new { error = "Location name is required." });

                loc.Name         = dto.Name.Trim();
                loc.Latitude     = dto.Latitude;
                loc.Longitude    = dto.Longitude;
                loc.Street       = dto.Street?.Trim();
                loc.City         = dto.City?.Trim();
                loc.Country      = dto.Country?.Trim();
                loc.PostalCode   = dto.PostalCode?.Trim();
                loc.RadiusMetres = dto.RadiusMetres > 0 ? dto.RadiusMetres : MinGeofenceRadius;

                await _context.SaveChangesAsync();
                return Ok(BuildResponse(loc));
            }
            catch (Exception ex) { _logger.LogError(ex, "Update Location {Id} failed", id); return StatusCode(500, new { error = ex.Message }); }
        }

        // ── PUT bulk radius ────────────────────────────────────────────────
        [HttpPut("bulk-radius")]
        public async Task<IActionResult> BulkUpdateRadius([FromBody] BulkRadiusDto dto)
        {
            try
            {
                if (dto.LocationIds == null || !dto.LocationIds.Any())
                    return BadRequest(new { error = "At least one location ID is required." });
                if (dto.RadiusMetres <= 0)
                    return BadRequest(new { error = "Radius must be greater than 0." });

                var locs = await _context.Locations.Where(l => dto.LocationIds.Contains(l.Id)).ToListAsync();
                foreach (var l in locs) l.RadiusMetres = dto.RadiusMetres;
                await _context.SaveChangesAsync();

                return Ok(new
                {
                    updated       = locs.Count,
                    radiusMetres  = dto.RadiusMetres,
                    geofenceReady = dto.RadiusMetres >= MinGeofenceRadius,
                    message       = dto.RadiusMetres < MinGeofenceRadius
                        ? $"Warning: radius below {MinGeofenceRadius}m minimum for automatic clock-in/out."
                        : "Radius updated successfully."
                });
            }
            catch (Exception ex) { _logger.LogError(ex, "BulkUpdateRadius failed"); return StatusCode(500, new { error = ex.Message }); }
        }

        // ── DELETE archive ─────────────────────────────────────────────────
        [HttpDelete("{id}")]
        public async Task<IActionResult> Archive(int id)
        {
            try
            {
                var loc = await _context.Locations.FindAsync(id);
                if (loc == null) return NotFound(new { error = "Location not found." });
                if (loc.IsArchived) return BadRequest(new { error = "Location is already archived." });
                loc.IsArchived = true;
                await _context.SaveChangesAsync();
                return Ok(new { message = "Location archived.", id });
            }
            catch (Exception ex) { _logger.LogError(ex, "Archive Location {Id} failed", id); return StatusCode(500, new { error = ex.Message }); }
        }

        // ── DELETE permanent ───────────────────────────────────────────────
        [HttpDelete("{id}/permanent")]
        public async Task<IActionResult> DeletePermanent(int id)
        {
            try
            {
                var loc = await _context.Locations.FindAsync(id);
                if (loc == null) return NotFound(new { error = "Location not found." });
                if (!loc.IsArchived) return BadRequest(new { error = "Only archived locations can be permanently deleted. Archive it first." });
                _context.Locations.Remove(loc);
                await _context.SaveChangesAsync();
                return Ok(new { message = "Location permanently deleted.", id });
            }
            catch (Exception ex) { _logger.LogError(ex, "DeletePermanent Location {Id} failed", id); return StatusCode(500, new { error = ex.Message }); }
        }

        // ── POST restore ───────────────────────────────────────────────────
        [HttpPost("{id}/restore")]
        public async Task<IActionResult> Restore(int id)
        {
            try
            {
                var loc = await _context.Locations.FindAsync(id);
                if (loc == null) return NotFound(new { error = "Location not found." });
                if (!loc.IsArchived) return BadRequest(new { error = "Location is not archived." });
                loc.IsArchived = false;
                await _context.SaveChangesAsync();
                return Ok(new { message = "Location restored.", location = BuildResponse(loc) });
            }
            catch (Exception ex) { _logger.LogError(ex, "Restore Location {Id} failed", id); return StatusCode(500, new { error = ex.Message }); }
        }

        // ── Helpers ────────────────────────────────────────────────────────
        private static Location MapFromDto(AddLocationDto dto) => new()
        {
            Name         = dto.Name.Trim(),
            Latitude     = dto.Latitude,
            Longitude    = dto.Longitude,
            Street       = dto.Street?.Trim(),
            City         = dto.City?.Trim(),
            Country      = dto.Country?.Trim(),
            PostalCode   = dto.PostalCode?.Trim(),
            RadiusMetres = dto.RadiusMetres > 0 ? dto.RadiusMetres : MinGeofenceRadius,
            IsMissing    = dto.IsMissing,
            IsArchived   = false,
            CreatedAt    = DateTime.UtcNow
        };

        private static object BuildResponse(Location l) => new
        {
            l.Id, l.Name, l.Latitude, l.Longitude,
            l.Street, l.City, l.Country, l.PostalCode,
            l.RadiusMetres, l.IsMissing, l.IsArchived, l.CreatedAt,
            geofenceReady  = l.RadiusMetres >= MinGeofenceRadius,
            displayAddress = string.Join(", ", new[] { l.Street, l.City, l.Country, l.PostalCode }
                                 .Where(s => !string.IsNullOrWhiteSpace(s)))
        };
    }
}
