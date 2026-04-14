using APM.StaffZen.API.Data;
using APM.StaffZen.API.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace APM.StaffZen.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class HolidayCalendarsController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        public HolidayCalendarsController(ApplicationDbContext context) => _context = context;

        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            try
            {
                var list = await _context.HolidayCalendars.OrderBy(c => c.Id).ToListAsync();
                return Ok(list);
            }
            catch
            {
                return Ok(new List<object>());
            }
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] HolidayCalendar cal)
        {
            cal.Id = 0;
            if (cal.IsDefault) await ClearDefaults();
            _context.HolidayCalendars.Add(cal);
            await _context.SaveChangesAsync();
            return Ok(cal);
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> Update(int id, [FromBody] HolidayCalendar cal)
        {
            var existing = await _context.HolidayCalendars.FindAsync(id);
            if (existing == null) return NotFound();
            if (cal.IsDefault && !existing.IsDefault) await ClearDefaults();
            existing.Name          = cal.Name;
            existing.Country       = cal.Country;
            existing.IsDefault     = cal.IsDefault;
            existing.HolidaysJson  = cal.HolidaysJson;
            existing.OrganizationId = cal.OrganizationId;
            await _context.SaveChangesAsync();
            return Ok(existing);
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(int id)
        {
            var c = await _context.HolidayCalendars.FindAsync(id);
            if (c == null) return NotFound();
            _context.HolidayCalendars.Remove(c);
            await _context.SaveChangesAsync();
            return Ok();
        }

        private async Task ClearDefaults()
        {
            var defaults = await _context.HolidayCalendars.Where(c => c.IsDefault).ToListAsync();
            foreach (var d in defaults) d.IsDefault = false;
            await _context.SaveChangesAsync();
        }
    }
}
