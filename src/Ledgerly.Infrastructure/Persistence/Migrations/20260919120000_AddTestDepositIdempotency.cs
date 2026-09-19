using Ledgerly.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ledgerly.Infrastructure.Persistence.Migrations;

[DbContext(typeof(LedgerlyDbContext))]
[Migration("20260919120000_AddTestDepositIdempotency")]
public partial class AddTestDepositIdempotency : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "test_deposit_operations",
            columns: table => new
            {
                wallet_id = table.Column<Guid>(type: "uuid", nullable: false),
                key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                journal_entry_id = table.Column<Guid>(type: "uuid", nullable: false),
                currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                balance = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_test_deposit_operations", x => new { x.wallet_id, x.key });
                table.ForeignKey("fk_test_deposit_operations_wallet", x => x.wallet_id,
                    "wallets", "id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("fk_test_deposit_operations_journal", x => x.journal_entry_id,
                    "journal_entries", "id", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "ux_test_deposit_operations_journal_entry_id",
            table: "test_deposit_operations",
            column: "journal_entry_id",
            unique: true);
        migrationBuilder.AddCheckConstraint(
            name: "ck_test_deposit_operations_amount_positive",
            table: "test_deposit_operations",
            sql: "amount > 0");
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropTable(name: "test_deposit_operations");
}
