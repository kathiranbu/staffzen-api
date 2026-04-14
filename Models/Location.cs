namespace APM.StaffZen.API.Models
{
    public class Location
    {
        public int     Id           { get; set; }
        public string  Name         { get; set; } = string.Empty;
        public double  Latitude     { get; set; }
        public double  Longitude    { get; set; }
        public string? Street       { get; set; }
        public string? City         { get; set; }
        public string? Country      { get; set; }
        public string? PostalCode   { get; set; }
        // Geofence radius in metres — minimum 300 for auto clock-in/out
        public int     RadiusMetres { get; set; } = 300;
        // True when added via "Add missing location" pin-drag flow
        public bool    IsMissing    { get; set; } = false;
        public bool    IsArchived   { get; set; } = false;
        public DateTime CreatedAt   { get; set; } = DateTime.UtcNow;
    }
}
