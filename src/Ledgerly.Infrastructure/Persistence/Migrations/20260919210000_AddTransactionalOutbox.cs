using Ledgerly.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ledgerly.Infrastructure.Persistence.Migrations;

[DbContext(typeof(LedgerlyDbContext))]
[Migration("20260919210000_AddTransactionalOutbox")]
public partial class AddTransactionalOutbox : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "outbox_messages",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                aggregate_id = table.Column<Guid>(type: "uuid", nullable: false),
                type = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                payload = table.Column<string>(type: "jsonb", nullable: false),
                occurred_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                processed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                attempt_count = table.Column<int>(type: "integer", nullable: false),
                last_error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_outbox_messages", x => x.id);
            });

        migrationBuilder.CreateIndex(
            name: "ix_outbox_messages_pending",
            table: "outbox_messages",
            column: "occurred_at_utc",
            filter: "processed_at_utc IS NULL");
        migrationBuilder.AddCheckConstraint(
            name: "ck_outbox_messages_attempt_count_non_negative",
            table: "outbox_messages",
            sql: "attempt_count >= 0");
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropTable(name: "outbox_messages");
}
