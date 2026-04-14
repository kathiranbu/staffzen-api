namespace APM.StaffZen.API.Dtos
{
    public class EmployeeListDto
    {
        public int     Id              { get; set; }
        public string  FullName        { get; set; } = null!;
        public string  Email           { get; set; } = null!;
        public string  Role            { get; set; } = null!;
        public string? MobileNumber    { get; set; }
        public string? CountryCode     { get; set; }
        public string? ProfileImageUrl { get; set; }
        public bool    IsActive        { get; set; }
        public int?    GroupId         { get; set; }
        public string? GroupName       { get; set; }

        /// <summary>True when the employee has enrolled face data.</summary>
        public bool    HasFaceData     { get; set; }
    }

    public class CreateEmployeeDto
    {
        public string    FullName     { get; set; } = null!;
        public string    Email        { get; set; } = null!;
        public string?   Role         { get; set; }
        public string?   MobileNumber { get; set; }
        public string?   CountryCode  { get; set; }
        public DateTime? DateOfBirth  { get; set; }
        public int?      GroupId      { get; set; }
    }

    public class UpdateEmployeeDto
    {
        public string    FullName     { get; set; } = null!;
        public string    Email        { get; set; } = null!;
        public string    Role         { get; set; } = null!;
        public string?   MobileNumber { get; set; }
        public string?   CountryCode  { get; set; }
        public DateTime? DateOfBirth  { get; set; }
        public bool      IsActive     { get; set; }
        public int?      GroupId      { get; set; }
    }

    public class UpdateGroupAssignmentDto
    {
        public int? GroupId { get; set; }
    }

    public class UpdateEmploymentDto
    {
        public string? Role    { get; set; }
        public int?    GroupId { get; set; }
    }
}
