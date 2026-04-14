using System.ComponentModel.DataAnnotations.Schema;

namespace APM.StaffZen.API.Models
{
    public class Employee
    {
        public int Id { get; set; }

        public required string FullName { get; set; }

        public required string Email { get; set; }

        public string? PasswordHash { get; set; }
        public string? PasswordSalt { get; set; }

        public required string Role { get; set; }

        public bool IsActive { get; set; }

        public DateTime? DateOfBirth { get; set; }

        public string? MobileNumber { get; set; }

        public string? CountryCode { get; set; }

        public string? ProfileImageUrl { get; set; }

        public int? GroupId { get; set; }  // Foreign key to Group

        /// <summary>Organization this employee belongs to. Null until onboarding is complete.</summary>
        public int? OrganizationId { get; set; }

        // Navigation
        public Organization? Organization { get; set; }

        public string? PasswordResetToken { get; set; }
        public DateTime? PasswordResetExpiry { get; set; }

        /// <summary>Firebase Cloud Messaging device token for push notifications.</summary>
        public string? FcmToken { get; set; }

        /// <summary>True once the user has completed the getting-started onboarding flow.</summary>
        public bool IsOnboarded { get; set; } = false;

        /// <summary>Short employee code used for identification (e.g. KAS-1, MO-1). Matches Jibble Member Code.</summary>
        public string? MemberCode { get; set; }

        /// <summary>Work schedule name assigned to this employee. Empty/null = use the org's default schedule.</summary>
        public string? WorkSchedule { get; set; } = "";

        // ── Facial Recognition ─────────────────────────────────────────────

        /// <summary>
        /// JSON-serialized float[128] face embedding produced by face-api.js.
        /// Stored as a string so no special column type is needed.
        /// Null when the employee has not enrolled their face.
        /// </summary>
        public string? FaceDescriptor { get; set; }

        /// <summary>Convenience flag — true when face data has been enrolled.</summary>
        [NotMapped]
        public bool HasFaceData => !string.IsNullOrEmpty(FaceDescriptor);
    }
}
