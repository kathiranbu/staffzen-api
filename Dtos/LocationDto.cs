namespace APM.StaffZen.API.Dtos
{
    public class AddLocationDto
    {
        public string  Name         { get; set; } = string.Empty;
        public double  Latitude     { get; set; }
        public double  Longitude    { get; set; }
        public string? Street       { get; set; }
        public string? City         { get; set; }
        public string? Country      { get; set; }
        public string? PostalCode   { get; set; }
        /// <summary>Geofence radius in metres. Minimum 300 for auto clock-in/out.</summary>
        public int     RadiusMetres { get; set; } = 300;
        /// <summary>True when added via pin-drag flow (forced true server-side on /missing endpoint).</summary>
        public bool    IsMissing    { get; set; } = false;
    }

    public class UpdateLocationDto
    {
        public string  Name         { get; set; } = string.Empty;
        public double  Latitude     { get; set; }
        public double  Longitude    { get; set; }
        public string? Street       { get; set; }
        public string? City         { get; set; }
        public string? Country      { get; set; }
        public string? PostalCode   { get; set; }
        public int     RadiusMetres { get; set; } = 300;
    }

    public class BulkRadiusDto
    {
        public List<int> LocationIds  { get; set; } = new();
        public int       RadiusMetres { get; set; } = 300;
    }
}
