using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ledgerly.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLedgerAccountPurpose : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_ledger_accounts_test_funding_currency",
                table: "ledger_accounts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_ledger_accounts_type_wallet",
                table: "ledger_accounts");

            migrationBuilder.AddColumn<int>(
                name: "purpose",
                table: "ledger_accounts",
                type: "integer",
                nullable: true);

            // The previous schema allowed exactly these two account roles.
            // Populate existing rows before enforcing the new required purpose.
            migrationBuilder.Sql("""
                UPDATE ledger_accounts
                SET purpose = CASE
                    WHEN wallet_id IS NOT NULL AND type = 2 THEN 1
                    WHEN wallet_id IS NULL AND type = 1 THEN 2
                END;
                """);

            migrationBuilder.AlterColumn<int>(
                name: "purpose",
                table: "ledger_accounts",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "ux_ledger_accounts_test_funding_currency",
                table: "ledger_accounts",
                column: "currency",
                unique: true,
                filter: "purpose = 2");

            migrationBuilder.AddCheckConstraint(
                name: "ck_ledger_accounts_purpose_type_wallet",
                table: "ledger_accounts",
                sql: "(purpose = 1 AND type = 2 AND wallet_id IS NOT NULL) OR (purpose = 2 AND type = 1 AND wallet_id IS NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_ledger_accounts_test_funding_currency",
                table: "ledger_accounts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_ledger_accounts_purpose_type_wallet",
                table: "ledger_accounts");

            migrationBuilder.DropColumn(
                name: "purpose",
                table: "ledger_accounts");

            migrationBuilder.CreateIndex(
                name: "ux_ledger_accounts_test_funding_currency",
                table: "ledger_accounts",
                column: "currency",
                unique: true,
                filter: "wallet_id IS NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_ledger_accounts_type_wallet",
                table: "ledger_accounts",
                sql: "(type = 1 AND wallet_id IS NULL) OR (type = 2 AND wallet_id IS NOT NULL)");
        }
    }
}
