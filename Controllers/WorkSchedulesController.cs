using APM.StaffZen.API.Data;
using APM.StaffZen.API.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace APM.StaffZen.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class WorkSchedulesController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        public WorkSchedulesController(ApplicationDbContext context) => _context = context;

        // ── Flat DTO — all strings nullable so model binding never fails ──────
        public class WorkScheduleDto
        {
            public int     Id                              { get; set; }
            public string? Name                            { get; set; }
            public bool    IsDefault                       { get; set; }
            public string? Arrangement                     { get; set; }
            public string? WorkingDays                     { get; set; }
            public string? DaySlotsJson                    { get; set; }
            public bool    IncludeBeforeStart              { get; set; }
            public int     WeeklyHours                     { get; set; }
            public int     WeeklyMinutes                   { get; set; }
            public string? SplitAt                         { get; set; }
            public string? BreaksJson                      { get; set; }
            public string? AutoDeductionsJson              { get; set; }
            public bool    DailyOvertime                   { get; set; }
            public bool    DailyOvertimeIsTime             { get; set; }
            public int     DailyOvertimeAfterHours         { get; set; }
            public int     DailyOvertimeAfterMins          { get; set; }
            public double  DailyOvertimeMultiplier         { get; set; }
            public bool    DailyDoubleOvertime             { get; set; }
            public int     DailyDoubleOTAfterHours         { get; set; }
            public int     DailyDoubleOTAfterMins          { get; set; }
            public double  DailyDoubleOTMultiplier         { get; set; }
            public bool    WeeklyOvertime                  { get; set; }
            public int     WeeklyOvertimeAfterHours        { get; set; }
            public int     WeeklyOvertimeAfterMins         { get; set; }
            public double  WeeklyOvertimeMultiplier        { get; set; }
            public bool    RestDayOvertime                 { get; set; }
            public double  RestDayOvertimeMultiplier       { get; set; }
            public bool    PublicHolidayOvertime           { get; set; }
            public double  PublicHolidayOvertimeMultiplier { get; set; }
            public int?    OrganizationId                  { get; set; }

            // ── Time Tracking Policy → Verification ───────────────────────────
            // RequireFaceVerification: "Require verification by Face Recognition" checkbox.
            //   When true → face-rec modal shown at clock-in/out (if face enrolled).
            // RequireSelfie: "Require selfies when clocking in and out" checkbox.
            //   Independent of RequireFaceVerification. Each is stored as-is.
            //   Can be true independently of face recognition.
            // UnusualBehavior: dropdown value for face-rec anomalies.
            //   Allowed values: "Blocked" | "Flagged" | "Allowed"  (default: "Blocked")
            public bool    RequireFaceVerification         { get; set; }
            public bool    RequireSelfie                   { get; set; }
            public string? UnusualBehavior                 { get; set; }  // "Blocked" | "Flagged" | "Allowed"
        }

        // Maps DTO to entity with safe string defaults
        private static WorkSchedule DtoToEntity(WorkScheduleDto d) => new WorkSchedule
        {
            Name                            = d.Name                            ?? "New Schedule",
            IsDefault                       = d.IsDefault,
            Arrangement                     = d.Arrangement                     ?? "Fixed",
            WorkingDays                     = d.WorkingDays                     ?? "Mon,Tue,Wed,Thu,Fri",
            DaySlotsJson                    = d.DaySlotsJson                    ?? "{}",
            IncludeBeforeStart              = d.IncludeBeforeStart,
            WeeklyHours                     = d.WeeklyHours,
            WeeklyMinutes                   = d.WeeklyMinutes,
            SplitAt                         = d.SplitAt                         ?? "00:00",
            BreaksJson                      = d.BreaksJson                      ?? "[]",
            AutoDeductionsJson              = d.AutoDeductionsJson              ?? "[]",
            DailyOvertime                   = d.DailyOvertime,
            DailyOvertimeIsTime             = d.DailyOvertimeIsTime,
            DailyOvertimeAfterHours         = d.DailyOvertimeAfterHours,
            DailyOvertimeAfterMins          = d.DailyOvertimeAfterMins,
            DailyOvertimeMultiplier         = d.DailyOvertimeMultiplier,
            DailyDoubleOvertime             = d.DailyDoubleOvertime,
            DailyDoubleOTAfterHours         = d.DailyDoubleOTAfterHours,
            DailyDoubleOTAfterMins          = d.DailyDoubleOTAfterMins,
            DailyDoubleOTMultiplier         = d.DailyDoubleOTMultiplier,
            WeeklyOvertime                  = d.WeeklyOvertime,
            WeeklyOvertimeAfterHours        = d.WeeklyOvertimeAfterHours,
            WeeklyOvertimeAfterMins         = d.WeeklyOvertimeAfterMins,
            WeeklyOvertimeMultiplier        = d.WeeklyOvertimeMultiplier,
            RestDayOvertime                 = d.RestDayOvertime,
            RestDayOvertimeMultiplier       = d.RestDayOvertimeMultiplier,
            PublicHolidayOvertime           = d.PublicHolidayOvertime,
            PublicHolidayOvertimeMultiplier = d.PublicHolidayOvertimeMultiplier,
            OrganizationId                  = d.OrganizationId,

            // Verification policy fields
            // Store each flag exactly as sent by the frontend.
            // The UI enforces: checking Face Recognition auto-checks Selfie and
            // unchecking it resets Selfie. So what arrives here is already correct.
            // We do NOT force-couple them server-side — that was causing RequireSelfie
            // to be stuck at true even after the admin unchecked Face Recognition.
            RequireFaceVerification         = d.RequireFaceVerification,
            RequireSelfie                   = d.RequireSelfie,
            UnusualBehavior                 = NormaliseUnusualBehavior(d.UnusualBehavior),
        };

        /// <summary>
        /// Ensures UnusualBehavior is always one of the three valid values.
        /// Falls back to "Blocked" for any unknown / null input.
        /// </summary>
        private static string NormaliseUnusualBehavior(string? value) =>
            value switch
            {
                "Flagged" => "Flagged",
                "Allowed" => "Allowed",
                _         => "Blocked",   // default & fallback
            };

        // GET api/WorkSchedules
        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            try
            {
                var list = await _context.WorkSchedules.OrderBy(s => s.Id).ToListAsync();
                return Ok(list);
            }
            catch (Exception ex)
            {
                // Log and surface the error — returning an empty list silently would
                // cause the selfie/face-verification policy to appear disabled even
                // when it is saved in the database.
                return StatusCode(500, new { error = ex.Message });
            }
        }

        // GET api/WorkSchedules/5
        [HttpGet("{id}")]
        public async Task<IActionResult> Get(int id)
        {
            var s = await _context.WorkSchedules.FindAsync(id);
            return s == null ? NotFound() : Ok(s);
        }

        // POST api/WorkSchedules — creates a new schedule
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] WorkScheduleDto dto)
        {
            try
            {
                if (dto.IsDefault) await ClearDefaults();
                var entity = DtoToEntity(dto);
                entity.Id = 0; // let DB auto-assign
                _context.WorkSchedules.Add(entity);
                await _context.SaveChangesAsync();
                return Ok(entity);
            }
            catch (Exception ex) { return StatusCode(500, new { error = ex.Message }); }
        }

        // PUT api/WorkSchedules/5 — updates an existing schedule
        [HttpPut("{id}")]
        public async Task<IActionResult> Update(int id, [FromBody] WorkScheduleDto dto)
        {
            try
            {
                var existing = await _context.WorkSchedules.FindAsync(id);
                if (existing == null) return NotFound();

                if (dto.IsDefault && !existing.IsDefault) await ClearDefaults();

                existing.Name                            = dto.Name                            ?? existing.Name;
                existing.IsDefault                       = dto.IsDefault;
                existing.Arrangement                     = dto.Arrangement                     ?? existing.Arrangement;
                existing.WorkingDays                     = dto.WorkingDays                     ?? existing.WorkingDays;
                existing.DaySlotsJson                    = dto.DaySlotsJson                    ?? existing.DaySlotsJson;
                existing.IncludeBeforeStart              = dto.IncludeBeforeStart;
                existing.WeeklyHours                     = dto.WeeklyHours;
                existing.WeeklyMinutes                   = dto.WeeklyMinutes;
                existing.SplitAt                         = dto.SplitAt                         ?? existing.SplitAt;
                existing.BreaksJson                      = dto.BreaksJson                      ?? existing.BreaksJson;
                existing.AutoDeductionsJson              = dto.AutoDeductionsJson              ?? existing.AutoDeductionsJson;
                existing.DailyOvertime                   = dto.DailyOvertime;
                existing.DailyOvertimeIsTime             = dto.DailyOvertimeIsTime;
                existing.DailyOvertimeAfterHours         = dto.DailyOvertimeAfterHours;
                existing.DailyOvertimeAfterMins          = dto.DailyOvertimeAfterMins;
                existing.DailyOvertimeMultiplier         = dto.DailyOvertimeMultiplier;
                existing.DailyDoubleOvertime             = dto.DailyDoubleOvertime;
                existing.DailyDoubleOTAfterHours         = dto.DailyDoubleOTAfterHours;
                existing.DailyDoubleOTAfterMins          = dto.DailyDoubleOTAfterMins;
                existing.DailyDoubleOTMultiplier         = dto.DailyDoubleOTMultiplier;
                existing.WeeklyOvertime                  = dto.WeeklyOvertime;
                existing.WeeklyOvertimeAfterHours        = dto.WeeklyOvertimeAfterHours;
                existing.WeeklyOvertimeAfterMins         = dto.WeeklyOvertimeAfterMins;
                existing.WeeklyOvertimeMultiplier        = dto.WeeklyOvertimeMultiplier;
                existing.RestDayOvertime                 = dto.RestDayOvertime;
                existing.RestDayOvertimeMultiplier       = dto.RestDayOvertimeMultiplier;
                existing.PublicHolidayOvertime           = dto.PublicHolidayOvertime;
                existing.PublicHolidayOvertimeMultiplier = dto.PublicHolidayOvertimeMultiplier;
                existing.OrganizationId                  = dto.OrganizationId;

                // Store verification policy exactly as sent by the frontend.
                // The UI cascade (face rec ON → selfie ON, face rec OFF → selfie OFF)
                // is handled in TimeTracking.razor. Do not re-couple here — it caused
                // RequireSelfie to be permanently stuck true after unchecking Face Recognition.
                existing.RequireFaceVerification         = dto.RequireFaceVerification;
                existing.RequireSelfie                   = dto.RequireSelfie;
                existing.UnusualBehavior                 = NormaliseUnusualBehavior(dto.UnusualBehavior);

                await _context.SaveChangesAsync();
                return Ok(existing);
            }
            catch (Exception ex) { return StatusCode(500, new { error = ex.Message }); }
        }

        // DELETE api/WorkSchedules/5
        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(int id)
        {
            var s = await _context.WorkSchedules.FindAsync(id);
            if (s == null) return NotFound();
            _context.WorkSchedules.Remove(s);
            await _context.SaveChangesAsync();
            return Ok();
        }

        private async Task ClearDefaults()
        {
            var defaults = await _context.WorkSchedules.Where(s => s.IsDefault).ToListAsync();
            foreach (var d in defaults) d.IsDefault = false;
            await _context.SaveChangesAsync();
        }
    }
}
