using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Plateia.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CheckoutELoginSocial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PedidoId",
                table: "Inscricoes",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "LoginsExternos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    UsuarioId = table.Column<int>(type: "int", nullable: false),
                    Provedor = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Chave = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Email = table.Column<string>(type: "varchar(180)", maxLength: 180, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CriadoEm = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LoginsExternos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LoginsExternos_Usuarios_UsuarioId",
                        column: x => x.UsuarioId,
                        principalTable: "Usuarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Pedidos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Codigo = table.Column<string>(type: "varchar(12)", maxLength: 12, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    EventoId = table.Column<int>(type: "int", nullable: false),
                    Nome = table.Column<string>(type: "varchar(160)", maxLength: 160, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Email = table.Column<string>(type: "varchar(180)", maxLength: 180, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Telefone = table.Column<string>(type: "varchar(15)", maxLength: 15, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Total = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false),
                    Desconto = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false),
                    CieloLinkId = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CieloLinkUrl = table.Column<string>(type: "varchar(300)", maxLength: 300, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CieloPedido = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CieloConsultadoEm = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CieloLinkValor = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: true),
                    CriadoEm = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Pedidos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Pedidos_Eventos_EventoId",
                        column: x => x.EventoId,
                        principalTable: "Eventos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_Inscricoes_PedidoId",
                table: "Inscricoes",
                column: "PedidoId");

            migrationBuilder.CreateIndex(
                name: "IX_LoginsExternos_Provedor_Chave",
                table: "LoginsExternos",
                columns: new[] { "Provedor", "Chave" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LoginsExternos_UsuarioId",
                table: "LoginsExternos",
                column: "UsuarioId");

            migrationBuilder.CreateIndex(
                name: "IX_Pedidos_CieloLinkId",
                table: "Pedidos",
                column: "CieloLinkId");

            migrationBuilder.CreateIndex(
                name: "IX_Pedidos_CieloPedido",
                table: "Pedidos",
                column: "CieloPedido");

            migrationBuilder.CreateIndex(
                name: "IX_Pedidos_Codigo",
                table: "Pedidos",
                column: "Codigo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Pedidos_EventoId",
                table: "Pedidos",
                column: "EventoId");

            migrationBuilder.AddForeignKey(
                name: "FK_Inscricoes_Pedidos_PedidoId",
                table: "Inscricoes",
                column: "PedidoId",
                principalTable: "Pedidos",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Inscricoes_Pedidos_PedidoId",
                table: "Inscricoes");

            migrationBuilder.DropTable(
                name: "LoginsExternos");

            migrationBuilder.DropTable(
                name: "Pedidos");

            migrationBuilder.DropIndex(
                name: "IX_Inscricoes_PedidoId",
                table: "Inscricoes");

            migrationBuilder.DropColumn(
                name: "PedidoId",
                table: "Inscricoes");
        }
    }
}
