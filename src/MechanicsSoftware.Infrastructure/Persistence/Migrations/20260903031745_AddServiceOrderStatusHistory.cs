using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MechanicsSoftware.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddServiceOrderStatusHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "service_order_status_history",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    service_order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    entered_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_service_order_status_history", x => x.id);
                    table.ForeignKey(
                        name: "FK_service_order_status_history_service_orders_service_order_id",
                        column: x => x.service_order_id,
                        principalTable: "service_orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_service_order_status_history_service_order_id_entered_at",
                table: "service_order_status_history",
                columns: new[] { "service_order_id", "entered_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "service_order_status_history");
        }
    }
}
