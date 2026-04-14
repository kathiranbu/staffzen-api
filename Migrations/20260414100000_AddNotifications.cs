using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace APM.StaffZen.API.Migrations
{
    /// <inheritdoc />
    public partial class AddNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Notifications",
                columns: table => new
                {
                    Id          = table.Column<int>(nullable: false)
                                       .Annotation("SqlServer:Identity", "1, 1"),
                    RecipientId = table.Column<int>(nullable: false),
                    Type        = table.Column<string>(maxLength: 50,  nullable: false, defaultValue: ""),
                    Title       = table.Column<string>(maxLength: 200, nullable: false, defaultValue: ""),
                    Message     = table.Column<string>(maxLength: 500, nullable: false, defaultValue: ""),
                    ReferenceId = table.Column<int>(nullable: false, defaultValue: 0),
                    IsRead      = table.Column<bool>(nullable: false, defaultValue: false),
                    CreatedAt   = table.Column<DateTime>(nullable: false, defaultValueSql: "GETUTCDATE()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Notifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Notifications_Employees_RecipientId",
                        column: x => x.RecipientId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_RecipientId_CreatedAt",
                table: "Notifications",
                columns: new[] { "RecipientId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "Notifications");
        }
    }
}
