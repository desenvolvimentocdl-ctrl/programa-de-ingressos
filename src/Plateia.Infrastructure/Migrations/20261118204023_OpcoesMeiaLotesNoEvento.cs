using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Plateia.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class OpcoesMeiaLotesNoEvento : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "TemLotes",
                table: "Eventos",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "TemMeia",
                table: "Eventos",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            // Eventos que já usavam lotes ou meia continuam com a opção ligada.
            migrationBuilder.Sql("UPDATE Eventos e SET e.TemLotes = 1 WHERE EXISTS (SELECT 1 FROM TiposIngresso t WHERE t.EventoId = e.Id AND (t.VendasAte IS NOT NULL OR t.VendasDe IS NOT NULL OR t.AbreAposAnterior = 1));");
            migrationBuilder.Sql("UPDATE Eventos e SET e.TemMeia = 1 WHERE EXISTS (SELECT 1 FROM TiposIngresso t WHERE t.EventoId = e.Id AND t.PermiteMeia = 1);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TemLotes",
                table: "Eventos");

            migrationBuilder.DropColumn(
                name: "TemMeia",
                table: "Eventos");
        }
    }
}
