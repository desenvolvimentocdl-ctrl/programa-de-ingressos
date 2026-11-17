using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Plateia.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class VendasCieloDivulgacao : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "VendasAte",
                table: "TiposIngresso",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Canal",
                table: "Inscricoes",
                type: "varchar(40)",
                maxLength: 40,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<DateTime>(
                name: "CieloConsultadoEm",
                table: "Inscricoes",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CieloLinkId",
                table: "Inscricoes",
                type: "varchar(64)",
                maxLength: 64,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "CieloLinkUrl",
                table: "Inscricoes",
                type: "varchar(300)",
                maxLength: 300,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "CieloPedido",
                table: "Inscricoes",
                type: "varchar(64)",
                maxLength: 64,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<int>(
                name: "CupomId",
                table: "Inscricoes",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Desconto",
                table: "Inscricoes",
                type: "decimal(10,2)",
                precision: 10,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "Cupons",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    EventoId = table.Column<int>(type: "int", nullable: false),
                    Codigo = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Tipo = table.Column<int>(type: "int", nullable: false),
                    Valor = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false),
                    Limite = table.Column<int>(type: "int", nullable: true),
                    ValidoAte = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    Ativo = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CriadoEm = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Cupons", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Cupons_Eventos_EventoId",
                        column: x => x.EventoId,
                        principalTable: "Eventos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Visitas",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    EventoId = table.Column<int>(type: "int", nullable: false),
                    Canal = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Dia = table.Column<DateTime>(type: "date", nullable: false),
                    Visitas = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Visitas", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Visitas_Eventos_EventoId",
                        column: x => x.EventoId,
                        principalTable: "Eventos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_Inscricoes_CieloLinkId",
                table: "Inscricoes",
                column: "CieloLinkId");

            migrationBuilder.CreateIndex(
                name: "IX_Inscricoes_CieloPedido",
                table: "Inscricoes",
                column: "CieloPedido");

            migrationBuilder.CreateIndex(
                name: "IX_Inscricoes_CupomId",
                table: "Inscricoes",
                column: "CupomId");

            migrationBuilder.CreateIndex(
                name: "IX_Cupons_EventoId_Codigo",
                table: "Cupons",
                columns: new[] { "EventoId", "Codigo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Visitas_EventoId_Canal_Dia",
                table: "Visitas",
                columns: new[] { "EventoId", "Canal", "Dia" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Inscricoes_Cupons_CupomId",
                table: "Inscricoes",
                column: "CupomId",
                principalTable: "Cupons",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Inscricoes_Cupons_CupomId",
                table: "Inscricoes");

            migrationBuilder.DropTable(
                name: "Cupons");

            migrationBuilder.DropTable(
                name: "Visitas");

            migrationBuilder.DropIndex(
                name: "IX_Inscricoes_CieloLinkId",
                table: "Inscricoes");

            migrationBuilder.DropIndex(
                name: "IX_Inscricoes_CieloPedido",
                table: "Inscricoes");

            migrationBuilder.DropIndex(
                name: "IX_Inscricoes_CupomId",
                table: "Inscricoes");

            migrationBuilder.DropColumn(
                name: "VendasAte",
                table: "TiposIngresso");

            migrationBuilder.DropColumn(
                name: "Canal",
                table: "Inscricoes");

            migrationBuilder.DropColumn(
                name: "CieloConsultadoEm",
                table: "Inscricoes");

            migrationBuilder.DropColumn(
                name: "CieloLinkId",
                table: "Inscricoes");

            migrationBuilder.DropColumn(
                name: "CieloLinkUrl",
                table: "Inscricoes");

            migrationBuilder.DropColumn(
                name: "CieloPedido",
                table: "Inscricoes");

            migrationBuilder.DropColumn(
                name: "CupomId",
                table: "Inscricoes");

            migrationBuilder.DropColumn(
                name: "Desconto",
                table: "Inscricoes");
        }
    }
}
