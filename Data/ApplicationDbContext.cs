using APM.StaffZen.API.Models;
using Microsoft.EntityFrameworkCore;

namespace APM.StaffZen.API.Data
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        public DbSet<Employee>                     Employees                    { get; set; }
        public DbSet<EmployeeInvite>               EmployeeInvites              { get; set; }
        public DbSet<Group>                        Groups                       { get; set; }
        public DbSet<TimeEntry>                    TimeEntries                  { get; set; }
        public DbSet<TimeEntryChangeLog>           TimeEntryChangeLogs          { get; set; }
        public DbSet<EmployeeNotificationSettings> EmployeeNotificationSettings { get; set; }
        public DbSet<Organization>                 Organizations                { get; set; }
        public DbSet<OrganizationMember>           OrganizationMembers          { get; set; }
        public DbSet<TimeOffPolicy>                TimeOffPolicies              { get; set; }
        public DbSet<TimeOffPolicyAssignment>      TimeOffPolicyAssignments     { get; set; }
        public DbSet<TimeOffRequest>               TimeOffRequests              { get; set; }
        public DbSet<LeaveRequest>                 LeaveRequests                { get; set; }
        public DbSet<Notification>                 Notifications                { get; set; }
        public DbSet<WorkSchedule>                 WorkSchedules                { get; set; }
        public DbSet<HolidayCalendar>              HolidayCalendars             { get; set; }
        public DbSet<Location>                     Locations                    { get; set; }
        public DbSet<EmployeeLocation>             EmployeeLocations            { get; set; }
        public DbSet<TimeTrackingPolicy>           TimeTrackingPolicies         { get; set; }
        public DbSet<AttendanceCorrectionRequest>  AttendanceCorrectionRequests { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<Employee>(b =>
            {
                b.HasOne(e => e.Organization)
                 .WithMany(o => o.Employees)
                 .HasForeignKey(e => e.OrganizationId)
                 .OnDelete(DeleteBehavior.SetNull);
            });

            modelBuilder.Entity<OrganizationMember>(b =>
            {
                b.HasIndex(m => new { m.OrganizationId, m.EmployeeId }).IsUnique();

                b.HasOne(m => m.Organization)
                 .WithMany()
                 .HasForeignKey(m => m.OrganizationId)
                 .OnDelete(DeleteBehavior.Cascade);

                b.HasOne(m => m.Employee)
                 .WithMany()
                 .HasForeignKey(m => m.EmployeeId)
                 .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<TimeEntryChangeLog>(b =>
            {
                b.HasOne(l => l.TimeEntry)
                 .WithMany()
                 .HasForeignKey(l => l.TimeEntryId)
                 .OnDelete(DeleteBehavior.Cascade);
                // EmployeeId stores who made the change (DB column renamed from ChangedByEmpId).
                b.Property(l => l.EmployeeId).HasColumnName("EmployeeId");
            });

            modelBuilder.Entity<TimeOffPolicyAssignment>(b =>
            {
                b.HasOne<TimeOffPolicy>()
                 .WithMany()
                 .HasForeignKey(a => a.PolicyId)
                 .OnDelete(DeleteBehavior.Cascade);
            });

            // Index for fast "latest location per employee" and "routes for day" queries
            modelBuilder.Entity<EmployeeLocation>(b =>
            {
                b.HasIndex(l => new { l.EmployeeId, l.RecordedAt });
            });

            // One policy row per organization
            modelBuilder.Entity<TimeTrackingPolicy>(b =>
            {
                b.HasIndex(p => p.OrganizationId).IsUnique();
                b.HasOne(p => p.Organization)
                 .WithMany()
                 .HasForeignKey(p => p.OrganizationId)
                 .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<AttendanceCorrectionRequest>(b =>
            {
                b.HasIndex(r => new { r.EmployeeId, r.AttendanceDate });
                b.HasIndex(r => new { r.OrganizationId, r.Status });

                b.HasOne(r => r.Employee)
                 .WithMany()
                 .HasForeignKey(r => r.EmployeeId)
                 .OnDelete(DeleteBehavior.Cascade);

                b.HasOne(r => r.TimeEntry)
                 .WithMany()
                 .HasForeignKey(r => r.TimeEntryId)
                 .OnDelete(DeleteBehavior.Restrict);
            });

            // Keyless projection used by PayPeriodController raw SQL queries
            modelBuilder.Entity<APM.StaffZen.API.Controllers.PayPeriodRow>()
                        .HasNoKey()
                        .ToTable((string?)null);
        }
    }
}
