using Plateia.Application;
using Plateia.Domain;
using Plateia.Infrastructure;
using Microsoft.AspNetCore.Identity;

namespace Plateia.Web.Infra;

/// <summary>
/// Dados FICTÍCIOS para demonstração (só com Demo__Criar=true e banco sem eventos).
/// Produtores, eventos, pessoas e e-mails são inventados; os e-mails usam o domínio reservado exemplo.com.
/// </summary>
public static class Demo
{
    static readonly string[] Nomes = ["Ana", "Bruno", "Camila", "Diego", "Eduarda", "Felipe", "Gabriela", "Henrique", "Isabela", "João",
        "Karina", "Lucas", "Mariana", "Natália", "Otávio", "Paula", "Rafael", "Sabrina", "Thiago", "Vanessa", "Wesley", "Yasmin",
        "Caio", "Letícia", "Gustavo", "Beatriz", "Rodrigo", "Priscila", "Marcelo", "Juliana", "Renan", "Aline", "Vinícius", "Larissa"];
    static readonly string[] Sobrenomes = ["Ribeiro", "Carvalho", "Nogueira", "Fontes", "Lima", "Azevedo", "Prado", "Matos", "Duarte", "Melo",
        "Souza", "Teixeira", "Rocha", "Freitas", "Rezende", "Moreira", "Pires", "Costa", "Barros", "Cunha", "Antunes", "Correia", "Mendes", "Alves"];
    static readonly string?[] Canais = ["instagram", "instagram", "instagram", "whatsapp", "whatsapp", null, "linkedin", "cartaz", "agenda"];

    public static async Task CriarAsync(AppDbContext db, IPasswordHasher<Usuario> hasher)
    {
        var rnd = new Random(42);
        var hoje = Hora.HojeLocal;
        DateTime Em(int dias, double hora) => Hora.ParaUtc(hoje.AddDays(dias).AddHours(hora));

        Usuario Produtor(string nome, string marca, string email, string cidade, string bio, string? ig, bool aprovado = true)
        {
            var u = new Usuario
            {
                Nome = nome, NomePublico = marca, Email = email, Telefone = "3499" + rnd.Next(1000000, 9999999), Cidade = cidade,
                Bio = bio, Instagram = ig, Slug = Utilidades.Slug(marca), Perfil = Perfil.Organizador, Aprovado = aprovado, EmailConfirmado = true
            };
            u.SenhaHash = hasher.HashPassword(u, "demo12345");
            db.Usuarios.Add(u);
            return u;
        }

        var marina = Produtor("Marina Duarte", "Marina Duarte Treinamentos", "demo@plateia.local", "Uberlândia",
            "Treinamentos de vendas e atendimento para pequenos negócios. Mais de 200 turmas desde 2015.", "marinaduarte.treina");
        var fermento = Produtor("Lucas Andrade", "Coletivo Fermento", "fermento@exemplo.com", "Uberlândia",
            "Cozinheiros e produtores locais que fazem festivais, oficinas e jantares com ingredientes do cerrado.", "coletivofermento");
        var hub = Produtor("Renata Okabe", "Hub Cerrado Tech", "hub@exemplo.com", "Uberlândia",
            "Comunidade de tecnologia do Triângulo Mineiro: meetups, workshops e o Cerrado Dev Day.", "hubcerradotech");
        var movimento = Produtor("Paula Lins", "Estúdio Movimento", "movimento@exemplo.com", "Uberlândia",
            "Yoga, corrida e respiração. Aulas ao ar livre e programas online.", "estudiomovimento");
        var palco = Produtor("Diego Sá", "Palco Aberto Produções", "palco@exemplo.com", "Uberaba",
            "Produtora de shows, teatro e humor no interior de Minas.", "palcoaberto");
        Produtor("Helena Prado", "Ateliê Ponto Cruz", "atelie@exemplo.com", "Araguari",
            "Oficinas de bordado e costura criativa. (Cadastro aguardando aprovação, para testar o fluxo do administrador.)", "atelie.pontocruz", aprovado: false);

        Evento Ev(Usuario org, string titulo, string? sub, CategoriaEvento cat, FormatoEvento fmt, int dias, double hora, double duracao,
            string cor, string desc, string local = "Online", string? end = null, string? bairro = null, string? cidade = null, string uf = "MG",
            string? classif = "Livre", bool destaque = false, StatusEvento status = StatusEvento.Publicado, string? instrucoes = null)
        {
            var e = new Evento
            {
                Titulo = titulo, Slug = Utilidades.Slug(titulo), Subtitulo = sub, Descricao = desc, Categoria = cat, Formato = fmt,
                Local = local, Endereco = end, Bairro = bairro, Cidade = fmt == FormatoEvento.Online ? null : cidade, Uf = fmt == FormatoEvento.Online ? null : uf,
                LinkOnline = fmt == FormatoEvento.Presencial ? null : "https://meet.exemplo.com/" + Utilidades.Slug(titulo),
                InicioEm = Em(dias, hora), FimEm = Em(dias, hora + duracao), Cor = cor, Classificacao = classif, Destaque = destaque,
                Status = status, InscricoesAbertas = status == StatusEvento.Publicado, Organizador = org, InstrucoesPagamento = instrucoes
            };
            db.Eventos.Add(e);
            return e;
        }
        static TipoIngresso T(string nome, decimal preco, int ordem, int? qtd = null, string? desc = null, EspacoEvento? esp = null, DateTime? ate = null) =>
            new() { Nome = nome, Preco = preco, Ordem = ordem, Quantidade = qtd, Descricao = desc, Espaco = esp, VendasAte = ate };
        static EspacoEvento S(string nome, int cap, int ordem, string? desc = null) => new() { Nome = nome, Capacidade = cap, Ordem = ordem, Descricao = desc };
        void Espacos(Evento e, params EspacoEvento[] xs) { e.Espacos.AddRange(xs); e.Capacidade = xs.Sum(x => x.Capacidade); }

        // ---------------- Marina Duarte Treinamentos ----------------
        var vendas = Ev(marina, "Vendas que não parecem vendas", "Palestra + bate-papo sobre relacionamento com clientes", CategoriaEvento.Negocios,
            FormatoEvento.Presencial, 9, 19, 3, "#FF6B4A",
            "Uma noite sobre como construir confiança antes de oferecer qualquer coisa.\n\nNa primeira parte, casos reais de pequenos negócios. Depois, perguntas abertas e networking com café.",
            "Teatro Vila Cerrado", "Av. das Palmeiras, 1200", "Centro", "Uberlândia", destaque: true,
            instrucoes: "Também aceitamos pagamento na secretaria do teatro, de segunda a sexta, das 9h às 18h.");
        var platVendas = S("Plateia", 100, 0, "Cadeiras numeradas, vista frontal do palco");
        var mesaVendas = S("Mesas de networking", 20, 1, "Mesas para 4 pessoas com café incluso, ao lado do palco");
        Espacos(vendas, platVendas, mesaVendas);
        vendas.Tipos = [T("2º lote", 80, 0, esp: platVendas), T("1º lote", 60, 1, esp: platVendas, ate: Em(-2, 23)),
            T("Meia / estudante", 40, 2, desc: "Apresente documento na entrada", esp: platVendas), T("Mesa de networking", 150, 3, esp: mesaVendas), T("Convidado", 0, 4, qtd: 10, esp: platVendas)];
        vendas.Cupons = [new Cupom { Codigo = "PARCEIRO20", Tipo = TipoDesconto.Percentual, Valor = 20, Limite = 30 }];

        Ev(marina, "Workshop de precificação", "Quanto cobrar sem medo", CategoriaEvento.Cursos, FormatoEvento.Presencial, 24, 13, 4, "#5B7CFA",
            "Planilha na mão: custos, margem e preço psicológico.", "Sala Ipê · Centro Empresarial Norte", "Rua das Acácias, 300", "Martins", "Uberlândia",
            status: StatusEvento.Rascunho).Tipos = [T("Participante", 150, 0)];
        var cafe = Ev(marina, "Café com empreendedores", null, CategoriaEvento.Networking, FormatoEvento.Presencial, -12, 8.5, 2, "#3DDC97",
            "Encontro mensal para trocar contatos e experiências.", "Padaria Grão Dourado", "Rua Goiás, 45", "Centro", "Uberlândia", status: StatusEvento.Encerrado);
        cafe.Capacidade = 40; cafe.Tipos = [T("Gratuito", 0, 0)];
        var lideranca = Ev(marina, "Liderança em 90 minutos", "Aula ao vivo para quem acabou de virar gestor", CategoriaEvento.Negocios, FormatoEvento.Online,
            5, 20, 1.5, "#A770EF", "Feedback, delegação e reuniões que terminam no horário. Com material de apoio em PDF.");
        lideranca.Capacidade = 300; lideranca.Tipos = [T("Acesso à aula", 39, 0), T("Aula + mentoria em grupo", 89, 1, qtd: 25)];

        // ---------------- Coletivo Fermento ----------------
        var festival = Ev(fermento, "Festival Sabores do Cerrado", "Dois dias de comida, música e produtores locais", CategoriaEvento.Gastronomia,
            FormatoEvento.Presencial, 16, 11, 10, "#FFB547",
            "Mais de 30 barracas com pratos do cerrado, cervejas artesanais e queijos da região. Aulas-show a cada hora e música ao vivo no fim da tarde.\n\nCrianças até 10 anos não pagam.",
            "Parque do Sabiá", "Rua Haia, s/n", "Tibery", "Uberlândia", destaque: true);
        var areaGeral = S("Área geral", 400, 0, "Acesso às barracas, palco e aulas-show");
        var degust = S("Mesa de degustação", 40, 1, "Menu harmonizado com 6 etapas, lugar marcado");
        Espacos(festival, areaGeral, degust);
        festival.Tipos = [T("Entrada", 30, 0, esp: areaGeral), T("Entrada + degustação", 120, 1, esp: degust)];
        var oficina = Ev(fermento, "Oficina de fermentação natural", "Do levain ao pão de casca crocante", CategoriaEvento.Cursos, FormatoEvento.Presencial,
            11, 9, 5, "#FF5C9A", "Turma pequena e mão na massa. Você leva para casa seu fermento e um pão assado na hora.",
            "Cozinha-escola Fermento", "Rua Tiradentes, 820", "Fundinho", "Uberlândia");
        oficina.Capacidade = 18; oficina.Tipos = [T("Participante", 180, 0)];
        var jantar = Ev(fermento, "Jantar a quatro mãos", "Dois chefs, um menu de sete tempos", CategoriaEvento.Gastronomia, FormatoEvento.Presencial,
            20, 20, 3, "#14110F", "Menu fechado com harmonização opcional. Restrições alimentares: avise ao se inscrever.",
            "Casa Jabuticaba", "Rua Segismundo Mendes, 77", "Mercês", "Uberaba", classif: "18+");
        jantar.Capacidade = 30; jantar.Tipos = [T("Menu completo", 260, 0)];

        // ---------------- Hub Cerrado Tech ----------------
        var devday = Ev(hub, "Cerrado Dev Day", "Palestras, workshops e muita conversa sobre software", CategoriaEvento.Tecnologia, FormatoEvento.Hibrido,
            13, 9, 9, "#2EC4D6",
            "O maior encontro de desenvolvedores do Triângulo. Trilhas de IA, dados, front-end e carreira.\n\nPalestras gratuitas (com transmissão). Workshops práticos pagos, com vagas limitadas.",
            "Centro de Convenções Triângulo", "Av. João Naves de Ávila, 1331", "Santa Mônica", "Uberlândia", destaque: true);
        var palcoDev = S("Palco principal", 250, 0, "Palestras das 9h às 18h");
        var salaWs = S("Sala de workshops", 40, 1, "Traga seu notebook");
        Espacos(devday, palcoDev, salaWs);
        devday.Tipos = [T("Palestras (gratuito)", 0, 0, esp: palcoDev), T("Workshop: IA na prática", 90, 1, qtd: 20, esp: salaWs), T("Workshop: APIs com .NET", 90, 2, qtd: 20, esp: salaWs)];
        var meetup = Ev(hub, "Meetup Dados & Café", "Três palestras rápidas e networking", CategoriaEvento.Tecnologia, FormatoEvento.Presencial,
            3, 19, 2.5, "#5B7CFA", "Edição de dados: dashboards que as pessoas realmente usam.", "Cowork Estação", "Av. Brasil, 150", "Centro", "Araguari");
        meetup.Capacidade = 60; meetup.Tipos = [T("Gratuito", 0, 0)];
        var ia = Ev(hub, "IA para pequenos negócios", "Ferramentas para atender, vender e organizar", CategoriaEvento.Tecnologia, FormatoEvento.Online,
            7, 19.5, 2, "#3DDC97", "Sem código: casos práticos para o dia a dia de lojas, clínicas e escritórios.");
        ia.Capacidade = 500; ia.Tipos = [T("Acesso ao vivo + gravação", 49, 0)];

        // ---------------- Estúdio Movimento ----------------
        var yoga = Ev(movimento, "Yoga no parque ao amanhecer", "Prática para todos os níveis", CategoriaEvento.Saude, FormatoEvento.Presencial,
            2, 6.5, 1.25, "#3DDC97", "Traga seu tapete e uma garrafa de água. Em caso de chuva, a aula vai para o dia seguinte.",
            "Parque Victório Siquierolli", "Av. Nossa Senhora do Carmo, 707", "Santa Rosa", "Uberlândia");
        yoga.Capacidade = 80; yoga.Tipos = [T("Gratuito", 0, 0)];
        var corrida = Ev(movimento, "Corrida noturna 5K", "Percurso iluminado com kit e medalha", CategoriaEvento.Esportes, FormatoEvento.Presencial,
            27, 20, 2, "#FF6B4A", "Retirada do kit na véspera, das 10h às 19h. Hidratação no km 2,5 e na chegada.",
            "Praça Clarimundo Carneiro", "Praça Clarimundo Carneiro, s/n", "Centro", "Uberlândia", destaque: true);
        corrida.Capacidade = 300;
        corrida.Tipos = [T("1º lote", 70, 0, ate: Em(-3, 23)), T("2º lote", 90, 1, ate: Em(20, 23)), T("3º lote", 110, 2)];
        corrida.Cupons = [new Cupom { Codigo = "ASSESSORIA10", Tipo = TipoDesconto.Valor, Valor = 10, Limite = 50 }];
        var respira = Ev(movimento, "Semana de respiração e foco", "5 encontros de 30 minutos", CategoriaEvento.Saude, FormatoEvento.Online,
            6, 7, 0.5, "#A770EF", "Técnicas simples para ansiedade e concentração, sempre às 7h.");
        respira.Capacidade = 200; respira.Tipos = [T("Semana completa", 59, 0)];

        // ---------------- Palco Aberto Produções ----------------
        var jazz = Ev(palco, "Noite de jazz no terraço", "Quarteto ao vivo com vista para a cidade", CategoriaEvento.Musica, FormatoEvento.Presencial,
            8, 21, 3, "#14110F", "Repertório de clássicos e autorais. Cozinha aberta até meia-noite.",
            "Terraço Mirante", "Av. Leopoldino de Oliveira, 3200", "Centro", "Uberaba", classif: "18+", destaque: true);
        var pista = S("Pista", 150, 0, "Em pé, perto do palco");
        var mesas = S("Mesas", 48, 1, "Mesa para 4 pessoas, com garçom");
        Espacos(jazz, pista, mesas);
        jazz.Tipos = [T("Pista", 70, 0, esp: pista), T("Lugar na mesa", 110, 1, esp: mesas)];
        var sarau = Ev(palco, "Sarau de poesia e música", "Microfone aberto: inscreva-se para ler ou tocar", CategoriaEvento.ArteCultura, FormatoEvento.Presencial,
            4, 19, 3, "#FF5C9A", "Noite livre com artistas locais. Quem quiser se apresentar escolhe o ingresso \"Vou me apresentar\".",
            "Casa de Cultura", "Praça Rui Barbosa, 10", "Centro", "Uberaba");
        sarau.Capacidade = 90; sarau.Tipos = [T("Público", 0, 0), T("Vou me apresentar", 0, 1, qtd: 15)];
        var teatro = Ev(palco, "O robô que queria dançar", "Teatro infantil com músicas ao vivo", CategoriaEvento.Infantil, FormatoEvento.Presencial,
            10, 16, 1.25, "#2EC4D6", "Espetáculo para crianças de 3 a 10 anos. Duração: 60 minutos.",
            "Teatro Vila Cerrado", "Av. das Palmeiras, 1200", "Centro", "Uberlândia");
        teatro.Capacidade = 120; teatro.Tipos = [T("Adulto", 40, 0), T("Criança", 20, 1, desc: "De 3 a 12 anos")];
        var standup = Ev(palco, "Rir é o melhor negócio", "Stand-up sobre a vida de quem empreende", CategoriaEvento.ArteCultura, FormatoEvento.Presencial,
            31, 21, 1.5, "#FFB547", "Uma hora de humor sobre boletos, clientes e reuniões que podiam ser um e-mail.",
            "Teatro do Bairro", "Rua dos Timbiras, 1500", "Funcionários", "Belo Horizonte", classif: "16+");
        standup.Capacidade = 220; standup.Tipos = [T("Inteira", 60, 0), T("Meia", 30, 1)];

        await db.SaveChangesAsync();

        // ---------------- Inscrições fictícias ----------------
        var usados = new HashSet<string>();
        void Inscrever(Evento ev, double ocupacao, bool passou = false)
        {
            var alvo = (int)(ev.Capacidade * ocupacao);
            var porEspaco = new Dictionary<int, int>();
            var porTipo = new Dictionary<int, int>();
            var tipos = ev.Tipos.Where(t => t.Ativo).ToList();
            for (var i = 0; i < alvo; i++)
            {
                var tipo = tipos[rnd.Next(10) < 6 ? 0 : rnd.Next(tipos.Count)];
                if (tipo.Quantidade is { } q && porTipo.GetValueOrDefault(tipo.Id) >= q) continue;
                if (tipo.Espaco is { } esp && porEspaco.GetValueOrDefault(esp.Id) >= esp.Capacidade) continue;
                string nome, email;
                do
                {
                    nome = $"{Nomes[rnd.Next(Nomes.Length)]} {Sobrenomes[rnd.Next(Sobrenomes.Length)]}";
                    email = Utilidades.Slug(nome).Replace("-", ".") + rnd.Next(10, 99) + "@exemplo.com";
                } while (!usados.Add(ev.Id + email));
                var criado = DateTime.UtcNow.AddMinutes(-rnd.Next(5, 60 * 24 * 20));
                if (tipo.VendasAte is { } va && criado > va) criado = va.AddHours(-rnd.Next(1, 72));
                var ins = new Inscricao
                {
                    EventoId = ev.Id, TipoIngressoId = tipo.Id, Nome = nome, Email = email, Telefone = "3499" + rnd.Next(1000000, 9999999),
                    Valor = tipo.Preco, Codigo = Utilidades.NovoCodigo(), CriadoEm = criado, Canal = Canais[rnd.Next(Canais.Length)],
                    Origem = rnd.Next(10) < 9 ? OrigemInscricao.Site : OrigemInscricao.Manual,
                    Pagamento = tipo.Gratuito ? StatusPagamento.Cortesia : rnd.Next(10) < 7 ? StatusPagamento.Pago : StatusPagamento.Pendente
                };
                if (ins.Pagamento == StatusPagamento.Pago)
                {
                    ins.PagoEm = criado.AddMinutes(rnd.Next(2, 300));
                    if (ins.PagoEm > DateTime.UtcNow) ins.PagoEm = DateTime.UtcNow.AddMinutes(-1);
                    ins.FormaPagamento = rnd.Next(10) switch { < 5 => "Cielo · Pix", < 9 => "Cielo · crédito", _ => "Dinheiro" };
                    if (ins.FormaPagamento.StartsWith("Cielo")) ins.CieloPedido = Guid.NewGuid().ToString("N");
                }
                if (ins.Pagamento == StatusPagamento.Cortesia) ins.PagoEm = criado;
                if (passou && rnd.Next(10) < 8) ins.CheckinEm = ev.InicioEm.AddMinutes(rnd.Next(-20, 40));
                if (rnd.Next(25) == 0) { ins.Status = StatusInscricao.Cancelada; ins.CanceladaEm = criado.AddHours(5) > DateTime.UtcNow ? DateTime.UtcNow : criado.AddHours(5); ins.CheckinEm = null; }
                else
                {
                    porTipo[tipo.Id] = porTipo.GetValueOrDefault(tipo.Id) + 1;
                    if (tipo.Espaco is { } e2) porEspaco[e2.Id] = porEspaco.GetValueOrDefault(e2.Id) + 1;
                }
                db.Inscricoes.Add(ins);
            }
        }
        Inscrever(vendas, .62);
        Inscrever(cafe, .85, passou: true);
        Inscrever(lideranca, .3);
        Inscrever(festival, .45);
        Inscrever(oficina, 1.3);      // esgotado
        Inscrever(jantar, 1.4);       // esgotado
        Inscrever(devday, .7);
        Inscrever(meetup, .5);
        Inscrever(ia, .2);
        Inscrever(yoga, .4);
        Inscrever(corrida, .55);
        Inscrever(respira, .25);
        Inscrever(jazz, .6);
        Inscrever(sarau, .3);
        Inscrever(teatro, .5);
        Inscrever(standup, .15);

        foreach (var (ev, fator) in new[] { (vendas, 1.0), (festival, 1.6), (devday, 1.3), (corrida, 1.1), (jazz, .9) })
            foreach (var (canal, total) in new[] { ("instagram", 420), ("whatsapp", 230), ("direto", 160), ("linkedin", 70), ("cartaz", 38), ("agenda", 90) })
                for (var d = 0; d < 7; d++)
                    db.Visitas.Add(new VisitaEvento { EventoId = ev.Id, Canal = canal, Dia = hoje.AddDays(-d), Visitas = (int)(total * fator / 7) + rnd.Next(0, 6) });

        await db.SaveChangesAsync();
    }
}
