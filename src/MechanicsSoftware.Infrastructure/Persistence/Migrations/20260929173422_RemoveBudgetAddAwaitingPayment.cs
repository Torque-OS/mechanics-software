using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MechanicsSoftware.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RemoveBudgetAddAwaitingPayment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "budgets");

            migrationBuilder.AddColumn<Guid>(
                name: "budget_id",
                table: "service_orders",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "budget_total",
                table: "service_orders",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "budget_id",
                table: "service_orders");

            migrationBuilder.DropColumn(
                name: "budget_total",
                table: "service_orders");

            migrationBuilder.CreateTable(
                name: "budgets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    service_order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    total = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_budgets", x => x.id);
                    table.ForeignKey(
                        name: "FK_budgets_service_orders_service_order_id",
                        column: x => x.service_order_id,
                        principalTable: "service_orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_budgets_service_order_id",
                table: "budgets",
                column: "service_order_id",
                unique: true);
        }
    }
}
