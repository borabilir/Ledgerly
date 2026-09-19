using Ledgerly.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ledgerly.Infrastructure.Persistence.Migrations;

[DbContext(typeof(LedgerlyDbContext))]
[Migration("20260919180000_AddWalletTransferIdempotency")]
public partial class AddWalletTransferIdempotency : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "wallet_transfers",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                source_wallet_id = table.Column<Guid>(type: "uuid", nullable: false),
                destination_wallet_id = table.Column<Guid>(type: "uuid", nullable: false),
                idempotency_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                journal_entry_id = table.Column<Guid>(type: "uuid", nullable: false),
                source_balance = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                destination_balance = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_wallet_transfers", x => x.id);
                table.ForeignKey("fk_wallet_transfers_source_wallet", x => x.source_wallet_id,
                    "wallets", "id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("fk_wallet_transfers_destination_wallet", x => x.destination_wallet_id,
                    "wallets", "id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("fk_wallet_transfers_journal", x => x.journal_entry_id,
                    "journal_entries", "id", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "ux_wallet_transfers_source_wallet_id_idempotency_key",
            table: "wallet_transfers",
            columns: new[] { "source_wallet_id", "idempotency_key" },
            unique: true);
        migrationBuilder.CreateIndex(
            name: "ix_wallet_transfers_destination_wallet_id",
            table: "wallet_transfers",
            column: "destination_wallet_id");
        migrationBuilder.CreateIndex(
            name: "ux_wallet_transfers_journal_entry_id",
            table: "wallet_transfers",
            column: "journal_entry_id",
            unique: true);
        migrationBuilder.AddCheckConstraint(
            name: "ck_wallet_transfers_amount_positive",
            table: "wallet_transfers",
            sql: "amount > 0");
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropTable(name: "wallet_transfers");
}
