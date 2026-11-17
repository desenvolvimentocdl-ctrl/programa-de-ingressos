using Plateia.Application;
using Plateia.Domain;
using Plateia.Infrastructure;
using Microsoft.AspNetCore.Identity;

namespace Plateia.Web.Infra;

/// <summary>Dados fictícios para demonstração (só com Demo__Criar=true e banco sem eventos).</summary>
public static class Demo
{
    static readonly string[] Nomes =
    [
        "Ana Paula Ribeiro", "Bruno Carvalho", "Camila Nogueira", "Diego Fontes", "Eduarda Lima", "Felipe Azevedo",
        "Gabriela Prado", "Henrique Matos", "Isabela Duarte", "João Victor Melo", "Karina Souza", "Lucas Teixeira",
        "Mariana Rocha", "Natália Freitas", "Otávio Rezende", "Paula Moreira", "Rafael Pires", "Sabrina Costa",
        "Thiago Barros", "Vanessa Cunha", "Wesley Antunes", "Yasmin Correia", "Caio Mendes", "Letícia Alves",
        "Gustavo Ramos", "Beatriz Moura", "Rodrigo Lacerda", "Priscila Tavares", "Marcelo Viana", "Juliana Brito"
    ];

    public static async Task CriarAsync(AppDbContext db, IPasswordHasher<Usuario> hasher)
    {
        var org = new Usuario { Nome = "Marina Duarte", Email = "demo@plateia.local", Telefone = "34999990000", Perfil = Perfil.Organizador };
        org.SenhaHash = hasher.HashPassword(org, "demo12345");
        db.Usuarios.Add(org);

        var agora = DateTime.UtcNow;
        var palestra = new Evento
        {
            Titulo = "Vendas que não parecem vendas", Slug = "vendas-que-nao-parecem-vendas",
            Subtitulo = "Palestra + bate-papo sobre relacionamento com clientes",
            Descricao = "Uma noite sobre como construir confiança antes de oferecer qualquer coisa.\n\nNa primeira parte, casos reais de pequenos negócios da cidade. Depois, perguntas abertas e networking com café.",
            Local = "Auditório principal", Endereco = "Av. Exemplo, 1000 · Centro", Capacidade = 120, Cor = "#FF6B4A",
            InicioEm = agora.Date.AddDays(9).AddHours(22), FimEm = agora.Date.AddDays(10).AddHours(1),
            Status = StatusEvento.Publicado, InscricoesAbertas = true,
            InstrucoesPagamento = "PIX: financeiro@exemplo.com.br\nEnvie o comprovante para o WhatsApp (34) 99999-0000 com o código do seu ingresso.",
            Organizador = org,
            Tipos =
            [
                new TipoIngresso { Nome = "Inteira", Preco = 80, Ordem = 0 },
                new TipoIngresso { Nome = "Meia / estudante", Descricao = "Apresente documento na entrada", Preco = 40, Ordem = 1 },
                new TipoIngresso { Nome = "Convidado", Preco = 0, Quantidade = 10, Ordem = 2 }
            ]
        };
        var workshop = new Evento
        {
            Titulo = "Workshop de precificação", Slug = "workshop-de-precificacao", Local = "Sala 2", Capacidade = 30, Cor = "#5B7CFA",
            InicioEm = agora.Date.AddDays(24).AddHours(13), Status = StatusEvento.Rascunho, Organizador = org,
            Tipos = [new TipoIngresso { Nome = "Participante", Preco = 150 }]
        };
        var passado = new Evento
        {
            Titulo = "Café com empreendedores", Slug = "cafe-com-empreendedores", Local = "Espaço de convivência", Capacidade = 40, Cor = "#3DDC97",
            InicioEm = agora.Date.AddDays(-12).AddHours(11), Status = StatusEvento.Encerrado, InscricoesAbertas = false, Organizador = org,
            Tipos = [new TipoIngresso { Nome = "Gratuito", Preco = 0 }]
        };
        db.Eventos.AddRange(palestra, workshop, passado);
        await db.SaveChangesAsync();

        var rnd = new Random(42);
        void Inscrever(Evento ev, int quantos, bool passou)
        {
            for (var i = 0; i < quantos; i++)
            {
                var nome = Nomes[i % Nomes.Length] + (i >= Nomes.Length ? $" {(char)('A' + i / Nomes.Length)}." : "");
                var tipo = ev.Tipos[rnd.Next(10) < 7 || ev.Tipos.Count == 1 ? 0 : rnd.Next(1, ev.Tipos.Count)];
                var criado = agora.AddMinutes(-rnd.Next(5, 60 * 24 * 14));
                var ins = new Inscricao
                {
                    EventoId = ev.Id, TipoIngressoId = tipo.Id, Nome = nome, Valor = tipo.Preco,
                    Email = Utilidades.Slug(nome).Replace("-", ".") + "@exemplo.com", Telefone = "3499" + rnd.Next(1000000, 9999999),
                    Codigo = Utilidades.NovoCodigo(), CriadoEm = criado, Origem = rnd.Next(10) < 8 ? OrigemInscricao.Site : OrigemInscricao.Manual,
                    Pagamento = tipo.Gratuito ? StatusPagamento.Cortesia : rnd.Next(10) < 7 ? StatusPagamento.Pago : StatusPagamento.Pendente
                };
                if (ins.Pagamento == StatusPagamento.Pago) { ins.PagoEm = criado.AddMinutes(rnd.Next(10, 600)); if (ins.PagoEm > agora) ins.PagoEm = agora.AddMinutes(-2); ins.FormaPagamento = "PIX"; }
                if (ins.Pagamento == StatusPagamento.Cortesia) ins.PagoEm = criado;
                if (passou && rnd.Next(10) < 8) ins.CheckinEm = ev.InicioEm.AddMinutes(rnd.Next(-20, 40));
                if (rnd.Next(20) == 0) { ins.Status = StatusInscricao.Cancelada; ins.CanceladaEm = criado.AddHours(5) > agora ? agora : criado.AddHours(5); ins.CheckinEm = null; }
                db.Inscricoes.Add(ins);
            }
        }
        Inscrever(palestra, 78, false);
        Inscrever(passado, 33, true);
        await db.SaveChangesAsync();
    }
}
