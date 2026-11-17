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
    public bool PagamentoOnline { get; init; }
    public bool TemCupons { get; init; }
    /// <summary>Canal de divulgação de onde a pessoa veio (vai junto na inscrição).</summary>
    public string? Canal { get; set; }
    public bool AceitaInscricoes => Evento.Status == StatusEvento.Publicado && Evento.InscricoesAbertas && Livres > 0;
}

public record OrganizadorLinha(int Id, string Nome, string Email, string? Telefone, Perfil Perfil, bool Ativo, int Eventos);

public record CartaoAgenda(string Slug, string Titulo, string? Subtitulo, string Local, DateTime InicioEm, string Cor, decimal MenorPreco, int Livres, bool Abertas);

public class PaginaDivulgacao
{
    public required Evento Evento { get; init; }
    public required string LinkBase { get; init; }
    public required List<LinhaFunil> Funil { get; init; }
    public required List<UsoCupom> Cupons { get; init; }
    public required int Confirmados { get; init; }
    public decimal MenorPreco { get; init; }
}
