namespace APM.StaffZen.API.Dtos
{
    /// <summary>
    /// Payload for enrolling or updating an employee's face descriptor.
    /// The descriptor is a JSON-serialized float[128] array produced by face-api.js.
    /// </summary>
    public class SaveFaceDescriptorDto
    {
        /// <summary>JSON string of float[128], e.g. "[0.123, -0.456, ...]"</summary>
        public string Descriptor { get; set; } = string.Empty;

        /// <summary>
        /// Optional base64-encoded JPEG snapshot captured at enrolment time.
        /// Saved to wwwroot/uploads/ so the UI can display the enrolled face photo.
        /// </summary>
        public string? EnrollPhoto { get; set; }
    }

    /// <summary>
    /// Payload for clocking in via facial recognition.
    /// Face matching is done client-side in JS; only the confirmed employee ID is sent here.
    /// </summary>
    public class FaceClockInDto
    {
        /// <summary>ID of the employee whose face was matched.</summary>
        public int EmployeeId { get; set; }

        /// <summary>Organization the kiosk belongs to (used to scope face data loading).</summary>
        public int OrganizationId { get; set; }

        /// <summary>
        /// Optional base64-encoded JPEG photo captured at clock-in time.
        /// Saved to wwwroot/uploads/ for audit purposes.
        /// </summary>
        public string? AuditPhotoBase64 { get; set; }
    }
}
