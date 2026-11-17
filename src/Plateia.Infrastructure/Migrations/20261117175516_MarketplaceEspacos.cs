using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Plateia.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class MarketplaceEspacos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "Aprovado",
                table: "Usuarios",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Bio",
                table: "Usuarios",
                type: "varchar(1000)",
                maxLength: 1000,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "Cidade",
                table: "Usuarios",
                type: "varchar(80)",
                maxLength: 80,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "Instagram",
                table: "Usuarios",
                type: "varchar(60)",
                maxLength: 60,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "NomePublico",
                table: "Usuarios",
                type: "varchar(120)",
                maxLength: 120,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "Site",
                table: "Usuarios",
                type: "varchar(200)",
                maxLength: 200,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "Slug",
                table: "Usuarios",
                type: "varchar(80)",
                maxLength: 80,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<int>(
                name: "EspacoId",
                table: "TiposIngresso",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Bairro",
                table: "Eventos",
                type: "varchar(80)",
                maxLength: 80,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<int>(
                name: "Categoria",
                table: "Eventos",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Cep",
                table: "Eventos",
                type: "varchar(9)",
                maxLength: 9,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "Cidade",
                table: "Eventos",
                type: "varchar(80)",
                maxLength: 80,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "Classificacao",
                table: "Eventos",
                type: "varchar(10)",
                maxLength: 10,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<bool>(
                name: "Destaque",
                table: "Eventos",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "Formato",
                table: "Eventos",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Imagem",
                table: "Eventos",
                type: "varchar(200)",
                maxLength: 200,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "LinkOnline",
                table: "Eventos",
                type: "varchar(300)",
                maxLength: 300,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "Uf",
                table: "Eventos",
                type: "varchar(2)",
                maxLength: 2,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Espacos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    EventoId = table.Column<int>(type: "int", nullable: false),
                    Nome = table.Column<string>(type: "varchar(80)", maxLength: 80, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Descricao = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Capacidade = table.Column<int>(type: "int", nullable: false),
                    Ordem = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Espacos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Espacos_Eventos_EventoId",
                        column: x => x.EventoId,
                        principalTable: "Eventos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_Usuarios_Slug",
                table: "Usuarios",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TiposIngresso_EspacoId",
                table: "TiposIngresso",
                column: "EspacoId");

            migrationBuilder.CreateIndex(
                name: "IX_Eventos_Cidade",
                table: "Eventos",
                column: "Cidade");

            migrationBuilder.CreateIndex(
                name: "IX_Eventos_Status_InicioEm",
                table: "Eventos",
                columns: new[] { "Status", "InicioEm" });

            migrationBuilder.CreateIndex(
                name: "IX_Espacos_EventoId",
                table: "Espacos",
                column: "EventoId");

            migrationBuilder.AddForeignKey(
                name: "FK_TiposIngresso_Espacos_EspacoId",
                table: "TiposIngresso",
                column: "EspacoId",
                principalTable: "Espacos",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TiposIngresso_Espacos_EspacoId",
                table: "TiposIngresso");

            migrationBuilder.DropTable(
                name: "Espacos");

            migrationBuilder.DropIndex(
                name: "IX_Usuarios_Slug",
                table: "Usuarios");

            migrationBuilder.DropIndex(
                name: "IX_TiposIngresso_EspacoId",
                table: "TiposIngresso");

            migrationBuilder.DropIndex(
                name: "IX_Eventos_Cidade",
                table: "Eventos");

            migrationBuilder.DropIndex(
                name: "IX_Eventos_Status_InicioEm",
                table: "Eventos");

            migrationBuilder.DropColumn(
                name: "Aprovado",
                table: "Usuarios");

            migrationBuilder.DropColumn(
                name: "Bio",
                table: "Usuarios");

            migrationBuilder.DropColumn(
                name: "Cidade",
                table: "Usuarios");

            migrationBuilder.DropColumn(
                name: "Instagram",
                table: "Usuarios");

            migrationBuilder.DropColumn(
                name: "NomePublico",
                table: "Usuarios");

            migrationBuilder.DropColumn(
                name: "Site",
                table: "Usuarios");

            migrationBuilder.DropColumn(
                name: "Slug",
                table: "Usuarios");

            migrationBuilder.DropColumn(
                name: "EspacoId",
                table: "TiposIngresso");

            migrationBuilder.DropColumn(
                name: "Bairro",
                table: "Eventos");

            migrationBuilder.DropColumn(
                name: "Categoria",
                table: "Eventos");

            migrationBuilder.DropColumn(
                name: "Cep",
                table: "Eventos");

            migrationBuilder.DropColumn(
                name: "Cidade",
                table: "Eventos");

            migrationBuilder.DropColumn(
                name: "Classificacao",
                table: "Eventos");

            migrationBuilder.DropColumn(
                name: "Destaque",
                table: "Eventos");

            migrationBuilder.DropColumn(
                name: "Formato",
                table: "Eventos");

            migrationBuilder.DropColumn(
                name: "Imagem",
                table: "Eventos");

            migrationBuilder.DropColumn(
                name: "LinkOnline",
                table: "Eventos");

            migrationBuilder.DropColumn(
                name: "Uf",
                table: "Eventos");
        }
    }
}
