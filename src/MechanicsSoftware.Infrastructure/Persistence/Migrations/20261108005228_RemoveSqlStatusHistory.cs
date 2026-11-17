using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MechanicsSoftware.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RemoveSqlStatusHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameTable(
                name: "service_order_status_history",
                newName: "service_order_status_history_legacy");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameTable(
                name: "service_order_status_history_legacy",
                newName: "service_order_status_history");
        }
    }
}
