namespace APM.StaffZen.API.Models
{
    /// <summary>
    /// Stores every GPS point recorded from an employee while they are clocked in.
    /// Points are collected every ~60 seconds from the browser's Geolocation API
    /// (or a future mobile app) and used to power the Live Locations map and
    /// the Routes timeline. Data older than 90 days is automatically purged by
    /// the LocationCleanupService background job.
    /// </summary>
    public class EmployeeLocation
    {
        public int      Id         { get; set; }
        public int      EmployeeId { get; set; }
        public double   Latitude   { get; set; }
        public double   Longitude  { get; set; }

        /// <summary>UTC timestamp of when this point was recorded.</summary>
        public DateTime RecordedAt { get; set; } = DateTime.UtcNow;

        /// <summary>GPS accuracy radius in metres (optional, supplied by browser).</summary>
        public float?   Accuracy   { get; set; }

        /// <summary>Movement speed in metres/second (optional, supplied by browser).</summary>
        public float?   Speed      { get; set; }
    }
}
