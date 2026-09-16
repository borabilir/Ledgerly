using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ledgerly.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLedgerPersistence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "ak_wallets_id_currency",
                table: "wallets",
                columns: new[] { "id", "currency" });

            migrationBuilder.CreateTable(
                name: "journal_entries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_journal_entries", x => x.id);
                    table.UniqueConstraint("ak_journal_entries_id_currency", x => new { x.id, x.currency });
                });

            migrationBuilder.CreateTable(
                name: "ledger_accounts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    wallet_id = table.Column<Guid>(type: "uuid", nullable: true),
                    type = table.Column<int>(type: "integer", nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ledger_accounts", x => x.id);
                    table.UniqueConstraint("ak_ledger_accounts_id_currency", x => new { x.id, x.currency });
                    table.CheckConstraint("ck_ledger_accounts_type_wallet", "(type = 1 AND wallet_id IS NULL) OR (type = 2 AND wallet_id IS NOT NULL)");
                    table.ForeignKey(
                        name: "fk_ledger_accounts_wallet_currency",
                        columns: x => new { x.wallet_id, x.currency },
                        principalTable: "wallets",
                        principalColumns: new[] { "id", "currency" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "postings",
                columns: table => new
                {
                    journal_entry_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    direction = table.Column<int>(type: "integer", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_postings", x => new { x.journal_entry_id, x.sequence });
                    table.CheckConstraint("ck_postings_amount_positive", "amount > 0");
                    table.CheckConstraint("ck_postings_direction", "direction IN (1, 2)");
                    table.CheckConstraint("ck_postings_sequence", "sequence >= 0");
                    table.ForeignKey(
                        name: "fk_postings_account_currency",
                        columns: x => new { x.account_id, x.currency },
                        principalTable: "ledger_accounts",
                        principalColumns: new[] { "id", "currency" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_postings_journal_currency",
                        columns: x => new { x.journal_entry_id, x.currency },
                        principalTable: "journal_entries",
                        principalColumns: new[] { "id", "currency" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ledger_accounts_wallet_id_currency",
                table: "ledger_accounts",
                columns: new[] { "wallet_id", "currency" });

            migrationBuilder.CreateIndex(
                name: "ux_ledger_accounts_test_funding_currency",
                table: "ledger_accounts",
                column: "currency",
                unique: true,
                filter: "wallet_id IS NULL");

            migrationBuilder.CreateIndex(
                name: "ux_ledger_accounts_wallet_id",
                table: "ledger_accounts",
                column: "wallet_id",
                unique: true,
                filter: "wallet_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_postings_account_id_currency",
                table: "postings",
                columns: new[] { "account_id", "currency" });

            migrationBuilder.CreateIndex(
                name: "IX_postings_journal_entry_id_currency",
                table: "postings",
                columns: new[] { "journal_entry_id", "currency" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "postings");

            migrationBuilder.DropTable(
                name: "ledger_accounts");

            migrationBuilder.DropTable(
                name: "journal_entries");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_wallets_id_currency",
                table: "wallets");
        }
    }
}
