using Plateia.Application;
using Plateia.Domain;

namespace Plateia.Web.ViewModels;

public record CartaoEvento(int Id, string Titulo, string Slug, string Local, DateTime InicioEm, int Capacidade, string Cor,
    StatusEvento Status, string Organizador, int Confirmados, int Pagos, int Pendentes, int Presentes);

public record OpcaoOrganizador(int Id, string Nome);

public class ListaParticipantes
{
    public required ResumoEvento Resumo { get; init; }
    public required List<Inscricao> Itens { get; init; }
    public required string Filtro { get; init; }
    public string? Busca { get; init; }
    public Evento Evento => Resumo.Evento;
}

public class PaginaEvento
{
    public required Evento Evento { get; init; }
    public required List<ResumoTipo> Tipos { get; init; }
    public required int Livres { get; init; }
    public required bool Previa { get; init; }
    public bool AceitaInscricoes => Evento.Status == StatusEvento.Publicado && Evento.InscricoesAbertas && Livres > 0;
}

public record OrganizadorLinha(int Id, string Nome, string Email, string? Telefone, Perfil Perfil, bool Ativo, int Eventos);
