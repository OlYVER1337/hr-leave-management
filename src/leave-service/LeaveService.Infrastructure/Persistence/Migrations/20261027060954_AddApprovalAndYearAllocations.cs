using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LeaveService.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddApprovalAndYearAllocations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LeaveRequestYearAllocations",
                columns: table => new
                {
                    LeaveRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Year = table.Column<int>(type: "int", nullable: false),
                    Days = table.Column<decimal>(type: "decimal(6,1)", precision: 6, scale: 1, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeaveRequestYearAllocations", x => new { x.LeaveRequestId, x.Year });
                    table.ForeignKey(
                        name: "FK_LeaveRequestYearAllocations_LeaveRequests_LeaveRequestId",
                        column: x => x.LeaveRequestId,
                        principalTable: "LeaveRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LeaveRequestYearAllocations");
        }
    }
}
