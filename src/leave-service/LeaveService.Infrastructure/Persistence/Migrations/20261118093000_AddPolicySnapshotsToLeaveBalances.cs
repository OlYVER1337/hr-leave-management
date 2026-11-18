using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LeaveService.Infrastructure.Persistence.Migrations;

public partial class AddPolicySnapshotsToLeaveBalances : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "CK_LeaveBalances_Entitled_NonNegative",
            table: "LeaveBalances");

        migrationBuilder.AddColumn<decimal>(
            name: "PolicyDays",
            table: "LeaveBalances",
            type: "decimal(6,1)",
            precision: 6,
            scale: 1,
            nullable: true);

        migrationBuilder.Sql("""
            UPDATE [LeaveBalances]
            SET [PolicyDays] = [Entitled];
            """);

        migrationBuilder.AlterColumn<decimal>(
            name: "PolicyDays",
            table: "LeaveBalances",
            type: "decimal(6,1)",
            precision: 6,
            scale: 1,
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(6,1)",
            oldPrecision: 6,
            oldScale: 1,
            oldNullable: true);

        migrationBuilder.DropColumn(
            name: "Entitled",
            table: "LeaveBalances");

        migrationBuilder.AddCheckConstraint(
            name: "CK_LeaveBalances_PolicyDays_NonNegative",
            table: "LeaveBalances",
            sql: "[PolicyDays] >= 0");

        migrationBuilder.AddColumn<int>(
            name: "PolicyVersionId",
            table: "LeaveRequestYearAllocations",
            type: "int",
            nullable: true);

        migrationBuilder.AddColumn<decimal>(
            name: "PolicyDaysAtCreation",
            table: "LeaveRequestYearAllocations",
            type: "decimal(6,1)",
            precision: 6,
            scale: 1,
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "CK_LeaveBalances_PolicyDays_NonNegative",
            table: "LeaveBalances");

        migrationBuilder.AddColumn<decimal>(
            name: "Entitled",
            table: "LeaveBalances",
            type: "decimal(6,1)",
            precision: 6,
            scale: 1,
            nullable: true);

        migrationBuilder.Sql("""
            UPDATE [LeaveBalances]
            SET [Entitled] =
                CASE
                    WHEN [PolicyDays] >= [Used] + [Pending] THEN [PolicyDays]
                    ELSE [Used] + [Pending]
                END;
            """);

        migrationBuilder.AlterColumn<decimal>(
            name: "Entitled",
            table: "LeaveBalances",
            type: "decimal(6,1)",
            precision: 6,
            scale: 1,
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(6,1)",
            oldPrecision: 6,
            oldScale: 1,
            oldNullable: true);

        migrationBuilder.DropColumn(
            name: "PolicyDays",
            table: "LeaveBalances");

        migrationBuilder.AddCheckConstraint(
            name: "CK_LeaveBalances_Entitled_NonNegative",
            table: "LeaveBalances",
            sql: "[Entitled] >= 0");

        migrationBuilder.DropColumn(
            name: "PolicyVersionId",
            table: "LeaveRequestYearAllocations");

        migrationBuilder.DropColumn(
            name: "PolicyDaysAtCreation",
            table: "LeaveRequestYearAllocations");
    }
}
