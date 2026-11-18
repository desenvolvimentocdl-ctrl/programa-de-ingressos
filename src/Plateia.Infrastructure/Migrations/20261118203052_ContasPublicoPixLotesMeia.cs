using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Plateia.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ContasPublicoPixLotesMeia : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "EmailConfirmado",
                table: "Usuarios",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "AbreAposAnterior",
                table: "TiposIngresso",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "PermiteMeia",
                table: "TiposIngresso",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "VendasDe",
                table: "TiposIngresso",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PixAnteriores",
                table: "Pedidos",
                type: "varchar(400)",
                maxLength: 400,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<DateTime>(
                name: "PixConsultadoEm",
                table: "Pedidos",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PixCopiaCola",
                table: "Pedidos",
                type: "varchar(1000)",
                maxLength: 1000,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<DateTime>(
                name: "PixGeradoEm",
                table: "Pedidos",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PixPaymentId",
                table: "Pedidos",
                type: "varchar(64)",
                maxLength: 64,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<decimal>(
                name: "PixValor",
                table: "Pedidos",
                type: "decimal(10,2)",
                precision: 10,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "Meia",
                table: "Inscricoes",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "CodigosEmail",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Email = table.Column<string>(type: "varchar(180)", maxLength: 180, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Finalidade = table.Column<int>(type: "int", nullable: false),
                    Hash = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ExpiraEm = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Tentativas = table.Column<int>(type: "int", nullable: false),
                    UsadoEm = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CriadoEm = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CodigosEmail", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_Pedidos_PixPaymentId",
                table: "Pedidos",
                column: "PixPaymentId");

            migrationBuilder.CreateIndex(
                name: "IX_CodigosEmail_Email_Finalidade",
                table: "CodigosEmail",
                columns: new[] { "Email", "Finalidade" });

            // Contas que já existiam (produtores e administrador) continuam com acesso total: e-mail tido como confirmado.
            migrationBuilder.Sql("UPDATE Usuarios SET EmailConfirmado = 1;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CodigosEmail");

            migrationBuilder.DropIndex(
                name: "IX_Pedidos_PixPaymentId",
                table: "Pedidos");

            migrationBuilder.DropColumn(
                name: "EmailConfirmado",
                table: "Usuarios");

            migrationBuilder.DropColumn(
                name: "AbreAposAnterior",
                table: "TiposIngresso");

            migrationBuilder.DropColumn(
                name: "PermiteMeia",
                table: "TiposIngresso");

            migrationBuilder.DropColumn(
                name: "VendasDe",
                table: "TiposIngresso");

            migrationBuilder.DropColumn(
                name: "PixAnteriores",
                table: "Pedidos");

            migrationBuilder.DropColumn(
                name: "PixConsultadoEm",
                table: "Pedidos");

            migrationBuilder.DropColumn(
                name: "PixCopiaCola",
                table: "Pedidos");

            migrationBuilder.DropColumn(
                name: "PixGeradoEm",
                table: "Pedidos");

            migrationBuilder.DropColumn(
                name: "PixPaymentId",
                table: "Pedidos");

            migrationBuilder.DropColumn(
                name: "PixValor",
                table: "Pedidos");

            migrationBuilder.DropColumn(
                name: "Meia",
                table: "Inscricoes");
        }
    }
}
