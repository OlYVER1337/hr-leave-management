using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LeaveService.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLeaveBalanceNonNegativeConstraints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddCheckConstraint(
                name: "CK_LeaveBalances_Entitled_NonNegative",
                table: "LeaveBalances",
                sql: "[Entitled] >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_LeaveBalances_Pending_NonNegative",
                table: "LeaveBalances",
                sql: "[Pending] >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_LeaveBalances_Used_NonNegative",
                table: "LeaveBalances",
                sql: "[Used] >= 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_LeaveBalances_Entitled_NonNegative",
                table: "LeaveBalances");

            migrationBuilder.DropCheckConstraint(
                name: "CK_LeaveBalances_Pending_NonNegative",
                table: "LeaveBalances");

            migrationBuilder.DropCheckConstraint(
                name: "CK_LeaveBalances_Used_NonNegative",
                table: "LeaveBalances");
        }
    }
}
