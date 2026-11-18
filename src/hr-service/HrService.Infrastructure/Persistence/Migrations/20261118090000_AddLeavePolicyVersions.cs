using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HrService.Infrastructure.Persistence.Migrations;

public partial class AddLeavePolicyVersions : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateOnly>(
            name: "EffectiveFrom",
            table: "LeavePolicies",
            type: "date",
            nullable: true);

        migrationBuilder.AddColumn<DateTime>(
            name: "CreatedAtUtc",
            table: "LeavePolicies",
            type: "datetime2",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "Reason",
            table: "LeavePolicies",
            type: "nvarchar(500)",
            maxLength: 500,
            nullable: true);

        // Legacy policies become the first version and must remain applicable to
        // requests created before this migration, including future-year leave requests.
        migrationBuilder.Sql("""
            UPDATE [LeavePolicies]
            SET [EffectiveFrom] = CONVERT(date, '20000101'),
                [CreatedAtUtc] = SYSUTCDATETIME(),
                [Reason] = N'Phiên bản được chuyển đổi từ chính sách hiện hữu'
            WHERE [EffectiveFrom] IS NULL;
            """);

        migrationBuilder.AlterColumn<DateOnly>(
            name: "EffectiveFrom",
            table: "LeavePolicies",
            type: "date",
            nullable: false,
            oldClrType: typeof(DateOnly),
            oldType: "date",
            oldNullable: true);

        migrationBuilder.AlterColumn<DateTime>(
            name: "CreatedAtUtc",
            table: "LeavePolicies",
            type: "datetime2",
            nullable: false,
            oldClrType: typeof(DateTime),
            oldType: "datetime2",
            oldNullable: true);

        migrationBuilder.AlterColumn<string>(
            name: "Reason",
            table: "LeavePolicies",
            type: "nvarchar(500)",
            maxLength: 500,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "nvarchar(500)",
            oldMaxLength: 500,
            oldNullable: true);

        migrationBuilder.DropIndex(
            name: "IX_LeavePolicies_LeaveTypeId_Year",
            table: "LeavePolicies");

        migrationBuilder.CreateIndex(
            name: "IX_LeavePolicies_LeaveTypeId_Year_EffectiveFrom",
            table: "LeavePolicies",
            columns: new[] { "LeaveTypeId", "Year", "EffectiveFrom" },
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Keep only the latest version for each type/year before restoring the
        // former unique constraint. Rolling this migration back loses version history.
        migrationBuilder.Sql("""
            WITH [RankedPolicies] AS
            (
                SELECT [Id],
                       ROW_NUMBER() OVER (
                           PARTITION BY [LeaveTypeId], [Year]
                           ORDER BY [EffectiveFrom] DESC, [CreatedAtUtc] DESC, [Id] DESC
                       ) AS [VersionNumber]
                FROM [LeavePolicies]
            )
            DELETE FROM [LeavePolicies]
            WHERE [Id] IN (
                SELECT [Id] FROM [RankedPolicies] WHERE [VersionNumber] > 1
            );
            """);

        migrationBuilder.DropIndex(
            name: "IX_LeavePolicies_LeaveTypeId_Year_EffectiveFrom",
            table: "LeavePolicies");

        migrationBuilder.CreateIndex(
            name: "IX_LeavePolicies_LeaveTypeId_Year",
            table: "LeavePolicies",
            columns: new[] { "LeaveTypeId", "Year" },
            unique: true);

        migrationBuilder.DropColumn(name: "EffectiveFrom", table: "LeavePolicies");
        migrationBuilder.DropColumn(name: "CreatedAtUtc", table: "LeavePolicies");
        migrationBuilder.DropColumn(name: "Reason", table: "LeavePolicies");
    }
}
