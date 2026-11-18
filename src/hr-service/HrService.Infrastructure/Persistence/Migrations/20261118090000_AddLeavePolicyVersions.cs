using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HrService.Infrastructure.Persistence.Migrations;

public partial class AddLeavePolicyVersions : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // This migration must support both fresh databases and databases where
        // an earlier deployment already added EffectiveFrom and its composite index.
        migrationBuilder.Sql("""
            IF COL_LENGTH(N'dbo.LeavePolicies', N'EffectiveFrom') IS NULL
            BEGIN
                ALTER TABLE [dbo].[LeavePolicies]
                ADD [EffectiveFrom] date NULL;
            END;

            IF COL_LENGTH(N'dbo.LeavePolicies', N'CreatedAtUtc') IS NULL
            BEGIN
                ALTER TABLE [dbo].[LeavePolicies]
                ADD [CreatedAtUtc] datetime2 NULL;
            END;

            IF COL_LENGTH(N'dbo.LeavePolicies', N'Reason') IS NULL
            BEGIN
                ALTER TABLE [dbo].[LeavePolicies]
                ADD [Reason] nvarchar(500) NULL;
            END;

            -- Backfill only missing values; preserve any existing policy metadata.
            UPDATE [dbo].[LeavePolicies]
            SET [EffectiveFrom] = COALESCE([EffectiveFrom], CONVERT(date, '20000101')),
                [CreatedAtUtc] = COALESCE([CreatedAtUtc], SYSUTCDATETIME()),
                [Reason] = COALESCE([Reason], N'Phiên bản được chuyển đổi từ chính sách hiện hữu')
            WHERE [EffectiveFrom] IS NULL
               OR [CreatedAtUtc] IS NULL
               OR [Reason] IS NULL;

            ALTER TABLE [dbo].[LeavePolicies]
            ALTER COLUMN [EffectiveFrom] date NOT NULL;

            ALTER TABLE [dbo].[LeavePolicies]
            ALTER COLUMN [CreatedAtUtc] datetime2 NOT NULL;

            ALTER TABLE [dbo].[LeavePolicies]
            ALTER COLUMN [Reason] nvarchar(500) NOT NULL;

            -- Older schema used (LeaveTypeId, Year); remove it if present.
            IF EXISTS
            (
                SELECT 1
                FROM sys.indexes
                WHERE [object_id] = OBJECT_ID(N'dbo.LeavePolicies')
                  AND [name] = N'IX_LeavePolicies_LeaveTypeId_Year'
            )
            BEGIN
                DROP INDEX [IX_LeavePolicies_LeaveTypeId_Year]
                ON [dbo].[LeavePolicies];
            END;

            -- Some deployed databases already have the target index.
            IF NOT EXISTS
            (
                SELECT 1
                FROM sys.indexes
                WHERE [object_id] = OBJECT_ID(N'dbo.LeavePolicies')
                  AND [name] = N'IX_LeavePolicies_LeaveTypeId_Year_EffectiveFrom'
            )
            BEGIN
                CREATE UNIQUE INDEX [IX_LeavePolicies_LeaveTypeId_Year_EffectiveFrom]
                ON [dbo].[LeavePolicies] ([LeaveTypeId], [Year], [EffectiveFrom]);
            END;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Refuse a destructive rollback when multiple versions exist for a policy.
        // An operator must export/resolve the history explicitly before downgrading.
        migrationBuilder.Sql("""
            IF EXISTS
            (
                SELECT 1
                FROM [LeavePolicies]
                GROUP BY [LeaveTypeId], [Year]
                HAVING COUNT(*) > 1
            )
            BEGIN
                THROW 51000,
                    'Cannot downgrade LeavePolicyVersions while version history exists. Resolve the history explicitly first.',
                    1;
            END;
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
