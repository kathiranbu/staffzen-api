namespace APM.StaffZen.API.Models
{
    public class HolidayCalendar
    {
        public int    Id          { get; set; }
        public string Name        { get; set; } = "";
        public string Country     { get; set; } = "";
        public bool   IsDefault   { get; set; } = false;
        public int?   OrganizationId { get; set; }

        // Holidays stored as JSON: [{"name":"...","date":"2026-01-01"}, ...]
        public string HolidaysJson { get; set; } = "[]";
    }
}
