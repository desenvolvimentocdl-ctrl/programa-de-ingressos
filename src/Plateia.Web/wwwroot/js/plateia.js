// Plateia · interações (sem bibliotecas)
(() => {
    "use strict";
    const $ = (s, el = document) => el.querySelector(s);
    const $$ = (s, el = document) => [...el.querySelectorAll(s)];
    const calmo = matchMedia("(prefers-reduced-motion: reduce)").matches;
    const csrf = $('meta[name="csrf"]')?.content ?? "";
    const real = new Intl.NumberFormat("pt-BR", { style: "currency", currency: "BRL", maximumFractionDigits: 0 });

    async function enviar(url, dados) {
        const r = await fetch(url, {
            method: "POST",
            headers: { "X-CSRF": csrf, "Accept": "application/json" },
            body: new URLSearchParams(dados)
        });
        const j = await r.json().catch(() => ({}));
        if (!r.ok) throw new Error(j.erro || "Não foi possível salvar. Tente de novo.");
        return j;
    }

    function aviso(texto, erro = false) {
        $$(".toast").forEach(t => t.remove());
        const t = document.createElement("div");
        t.className = "toast" + (erro ? " erro" : "");
        t.setAttribute("role", erro ? "alert" : "status");
        t.textContent = texto;
        document.body.append(t);
        setTimeout(() => t.remove(), 4600);
    }

    // Números que "contam" até o valor, só quando aparecem na tela.
    function contar(el) {
        const alvo = parseFloat(el.dataset.contar);
        const dinheiro = el.dataset.formato === "dinheiro";
        const fmt = v => dinheiro ? real.format(v) : Math.round(v).toLocaleString("pt-BR");
        if (calmo || !isFinite(alvo) || alvo === 0) { el.textContent = fmt(alvo || 0); return; }
        const dur = 900, ini = performance.now();
        const passo = t => {
            const k = Math.min(1, (t - ini) / dur);
            el.textContent = fmt(alvo * (1 - Math.pow(1 - k, 4)));
            if (k < 1) requestAnimationFrame(passo);
        };
        requestAnimationFrame(passo);
    }
    const obs = new IntersectionObserver(es => es.forEach(e => {
        if (e.isIntersecting) { contar(e.target); obs.unobserve(e.target); }
    }));
    $$("[data-contar]").forEach(el => obs.observe(el));

    // Copiar link / código
    document.addEventListener("click", async e => {
        const b = e.target.closest("[data-copiar]");
        if (!b) return;
        e.preventDefault();
        try { await navigator.clipboard.writeText(b.dataset.copiar); aviso(b.dataset.aviso || "Copiado."); }
        catch { prompt("Copie:", b.dataset.copiar); }
    });

    // Diálogos
    document.addEventListener("click", e => {
        const a = e.target.closest("[data-abrir]");
        if (a) { e.preventDefault(); $(a.dataset.abrir)?.showModal(); }
        const f = e.target.closest("[data-fechar]");
        if (f) { e.preventDefault(); f.closest("dialog")?.close(); }
    });
    $$("dialog").forEach(d => d.addEventListener("click", e => { if (e.target === d) d.close(); }));

    // Confirmação antes de ações destrutivas
    document.addEventListener("submit", e => {
        const msg = e.submitter?.dataset.confirmar || e.target.dataset.confirmar;
        if (msg && !confirm(msg)) e.preventDefault();
    });

    // Fecha menus <details> ao clicar fora
    document.addEventListener("click", e => {
        $$("details.menu[open]").forEach(d => { if (!d.contains(e.target)) d.open = false; });
    });

    // ---------- Participantes: seleção e ações em lote ----------
    const barra = $(".barra-lote");
    if (barra) {
        const caixas = () => $$("input[data-sel]");
        const atualizar = () => {
            const marcadas = caixas().filter(c => c.checked);
            caixas().forEach(c => c.closest("tr").classList.toggle("sel", c.checked));
            $("b", barra).textContent = `${marcadas.length} selecionado${marcadas.length === 1 ? "" : "s"}`;
            barra.classList.toggle("on", marcadas.length > 0);
            const todos = $("#sel-todos");
            if (todos) { todos.checked = marcadas.length > 0 && marcadas.length === caixas().length; todos.indeterminate = marcadas.length > 0 && !todos.checked; }
        };
        document.addEventListener("change", e => {
            if (e.target.matches("input[data-sel]")) atualizar();
            if (e.target.id === "sel-todos") { caixas().forEach(c => c.checked = e.target.checked); atualizar(); }
        });
        $("form", barra).addEventListener("submit", e => {
            $$("input[name=ids]", e.target).forEach(i => i.remove());
            caixas().filter(c => c.checked).forEach(c => {
                const h = document.createElement("input");
                h.type = "hidden"; h.name = "ids"; h.value = c.value;
                e.target.append(h);
            });
        });
        $("[data-limpar]", barra)?.addEventListener("click", () => { caixas().forEach(c => c.checked = false); atualizar(); });
    }

    // Presença com um clique, com carimbo
    document.addEventListener("click", async e => {
        const b = e.target.closest("[data-presenca]");
        if (!b) return;
        const presente = b.dataset.presenca === "1";
        const celula = b.closest(".presenca");
        try {
            const r = await enviar(b.dataset.url, { presente });
            celula.innerHTML = presente
                ? `<button type="button" class="carimbo batendo" data-presenca="0" data-url="${b.dataset.url}" title="Desfazer presença">PRESENTE<small>${r.hora}</small></button>`
                : `<button type="button" class="btn-entrar" data-presenca="1" data-url="${b.dataset.url}">Marcar entrada</button>`;
            if (presente && navigator.vibrate) navigator.vibrate(30);
        } catch (err) { aviso(err.message, true); }
    });

    // ---------- Painel: feed ao vivo ----------
    const feed = $("[data-aovivo]");
    if (feed) {
        const vistos = new Set($$("li[data-chave]", feed).map(l => l.dataset.chave));
        const buscar = async () => {
            if (document.hidden) return;
            try {
                const r = await fetch(feed.dataset.aovivo, { headers: { "Accept": "text/html" } });
                if (!r.ok) return;
                const tmp = document.createElement("div");
                tmp.innerHTML = await r.text();
                const novo = $(".feed", tmp) ?? tmp.firstElementChild;
                if (!novo) return;
                $$("li[data-chave]", novo).forEach(li => { if (!vistos.has(li.dataset.chave)) { li.classList.add("novo"); vistos.add(li.dataset.chave); } });
                feed.replaceChildren(novo);
            } catch { /* sem rede: tenta no próximo ciclo */ }
        };
        setInterval(buscar, 30000);
        document.addEventListener("visibilitychange", () => { if (!document.hidden) buscar(); });
    }

    // ---------- Formulário do evento ----------
    const formEvento = $("#form-evento");
    if (formEvento) {
        const previa = $(".previa .cartaz");
        const tinta = hex => {
            const c = i => { const v = parseInt(hex.substr(i, 2), 16) / 255; return v <= .03928 ? v / 12.92 : Math.pow((v + .055) / 1.055, 2.4); };
            return (.2126 * c(1) + .7152 * c(3) + .0722 * c(5)) > .32 ? "#14110F" : "#FFFFFF";
        };
        const pintar = cor => {
            if (!/^#[0-9a-f]{6}$/i.test(cor)) return;
            previa.style.setProperty("--cor", cor);
            previa.style.setProperty("--tinta", tinta(cor));
            $("#cor-final").value = cor;
        };
        formEvento.addEventListener("change", e => {
            if (e.target.name === "cor-opcao") pintar(e.target.value);
            if (e.target.id === "cor-livre") { pintar(e.target.value); $$("input[name=cor-opcao]").forEach(r => r.checked = false); }
        });
        $("#cor-livre")?.addEventListener("input", e => pintar(e.target.value));
        const espelhar = (campo, alvo, padrao) => {
            const c = $(`[name=${campo}]`, formEvento), a = $(alvo);
            if (c && a) c.addEventListener("input", () => a.textContent = c.value.trim() || padrao);
        };
        espelhar("titulo", "#previa-titulo", "Nome do evento");
        espelhar("local", "#previa-local", "Local");

        const lista = $("#tipos");
        $("#add-tipo")?.addEventListener("click", () => {
            const n = $("#modelo-tipo").content.firstElementChild.cloneNode(true);
            lista.append(n);
            $("input[name=tipo_nome]", n).focus();
        });
        lista?.addEventListener("click", e => {
            const b = e.target.closest("[data-remover-tipo]");
            if (!b) return;
            const linha = b.closest(".tipo-linha");
            if ($$(".tipo-linha:not(.removido)", lista).length <= 1) { aviso("O evento precisa de pelo menos um tipo de ingresso.", true); return; }
            if (!$("input[name=tipo_id]", linha).value) { linha.remove(); return; }
            $("input[name=tipo_remover]", linha).value = "1";
            $$("[required]", linha).forEach(i => i.required = false);
            linha.classList.add("removido");
        });
    }

    // Copiar o texto de um campo (mensagem pronta)
    document.addEventListener("click", async e => {
        const b = e.target.closest("[data-copiar-de]");
        if (!b) return;
        const v = $(b.dataset.copiarDe)?.value ?? "";
        try { await navigator.clipboard.writeText(v); aviso("Mensagem copiada."); } catch { prompt("Copie:", v); }
    });

    // ---------- Página pública: checkout (vários ingressos, um por participante; inteira ou meia) ----------
    let cupomAtual = null;              // { tipo: "Percentual" | "Valor", valor } quando há cupom válido
    let recalcularCheckout = () => { };
    const formCk = $("#form-inscricao.checkout");
    if (formCk) {
        const maxPedido = +formCk.dataset.max || 10;
        const maxTotal = Math.min(maxPedido, +formCk.dataset.livres || maxPedido);
        const linhas = $$(".opcao.qtd", formCk);
        const chave = (tipo, meia) => `${tipo}-${meia ? 1 : 0}`;
        const linha = p => linhas.find(o => o.dataset.tipo == p.tipo && (o.dataset.meia === "1") === !!p.meia);
        const ordem = p => linhas.indexOf(linha(p));
        const maxDoTipo = tipo => +(linhas.find(o => o.dataset.tipo == tipo)?.dataset.max || 0);
        const lista = $("[data-participantes]", formCk), bloco = $("[data-participantes-bloco]", formCk);
        const brl = new Intl.NumberFormat("pt-BR", { style: "currency", currency: "BRL" });
        const fmt = v => brl.format(v).replace(",00", "");
        let estado = [];
        try { estado = (JSON.parse(formCk.dataset.estado || "{}").participantes || []).filter(p => linha(p) && maxDoTipo(p.tipo) > 0); } catch { }
        if (estado.length && !estado.some(p => p.meu)) estado[0].meu = true;

        const qtdLinha = (tipo, meia) => estado.filter(p => chave(p.tipo, p.meia) === chave(tipo, meia)).length;
        const qtdTipo = tipo => estado.filter(p => p.tipo == tipo).length;
        const precoFinal = p => {
            const base = +linha(p).dataset.valor;
            if (!cupomAtual || base <= 0) return base;
            const desc = Math.min(base, cupomAtual.tipo === "Percentual" ? base * cupomAtual.valor / 100 : cupomAtual.valor);
            return Math.round((base - desc) * 100) / 100;
        };

        // Guarda o que já foi digitado antes de redesenhar a lista.
        const lerTela = () => $$(".participante", lista).forEach((f, i) => {
            const p = estado[i]; if (!p) return;
            p.nome = $("[data-campo=nome]", f).value; p.email = $("[data-campo=email]", f).value;
            p.telefone = $("[data-campo=telefone]", f).value; p.meu = $("[data-meu]", f).checked;
        });

        const campo = (rotulo, nome, i, attrs) => {
            const l = document.createElement("label");
            l.className = "campo";
            l.innerHTML = `${rotulo}<input data-campo="${nome}" name="p[${i}].${nome[0].toUpperCase() + nome.slice(1)}" ${attrs}>`;
            return l;
        };
        const oculto = (nome, valor) => { const h = document.createElement("input"); h.type = "hidden"; h.name = nome; h.value = valor; return h; };

        function desenhar() {
            lista.replaceChildren();
            const contagem = {};
            estado.forEach((p, i) => {
                const k = chave(p.tipo, p.meia);
                contagem[k] = (contagem[k] || 0) + 1;
                const f = document.createElement("fieldset");
                f.className = "participante";
                const lg = document.createElement("legend");
                const titulo = document.createElement("span");
                titulo.textContent = `Ingresso ${i + 1} · ${linha(p).dataset.nome}` + (qtdLinha(p.tipo, p.meia) > 1 ? ` (${contagem[k]})` : "");
                const valor = document.createElement("small");
                valor.className = "valor-ing";
                lg.append(titulo, valor);

                const meu = document.createElement("label");
                meu.className = "check";
                meu.innerHTML = `<input type="checkbox" data-meu name="p[${i}].Meu" value="true"> Este ingresso é meu (usa os meus dados)`;

                const campos = document.createElement("div");
                campos.className = "campos";
                const dupla = document.createElement("div");
                dupla.className = "linha2";
                dupla.append(
                    campo('E-mail <small>opcional</small>', "email", i, 'type="email" maxlength="180" autocomplete="off"'),
                    campo('WhatsApp <small>opcional</small>', "telefone", i, 'inputmode="tel" autocomplete="off"'));
                campos.append(campo("Nome completo do participante", "nome", i, 'maxlength="160" autocomplete="off"'), dupla);

                f.append(lg, oculto(`p[${i}].TipoId`, p.tipo), oculto(`p[${i}].Meia`, p.meia ? "true" : "false"), meu, campos);
                if (p.meia) {
                    const av = document.createElement("small");
                    av.className = "aviso-meia";
                    av.textContent = "Meia-entrada: o participante apresenta o documento que dá direito à meia na entrada.";
                    f.append(av);
                }
                lista.append(f);
                $("[data-campo=nome]", f).value = p.nome || "";
                $("[data-campo=email]", f).value = p.email || "";
                $("[data-campo=telefone]", f).value = p.telefone || "";
                const caixa = $("[data-meu]", f);
                caixa.checked = !!p.meu;
                const exigir = () => $("[data-campo=nome]", f).required = !caixa.checked;
                exigir();
                caixa.addEventListener("change", () => {
                    // Só um ingresso pode ser "meu".
                    if (caixa.checked) $$("[data-meu]", lista).forEach(o => { if (o !== caixa && o.checked) { o.checked = false; o.dispatchEvent(new Event("change")); } });
                    exigir();
                    if (!caixa.checked) $("[data-campo=nome]", f).focus();
                });
            });
            bloco.hidden = estado.length === 0;
            atualizar();
        }

        function atualizar() {
            const total = estado.length;
            linhas.forEach(o => {
                const out = $("output", o);
                if (!out) return;
                const tipo = o.dataset.tipo, meia = o.dataset.meia === "1", n = qtdLinha(tipo, meia);
                out.textContent = n;
                o.classList.toggle("marcada", n > 0);
                $("[data-menos]", o).disabled = n === 0;
                $("[data-mais]", o).disabled = qtdTipo(tipo) >= maxDoTipo(tipo) || total >= maxTotal;
            });
            const valor = estado.reduce((s, p) => s + precoFinal(p), 0);
            $$(".participante .valor-ing", lista).forEach((s, i) => { const v = precoFinal(estado[i]); s.textContent = v <= 0 ? "Gratuito" : fmt(v); });
            const caixaTotal = $("[data-total]", formCk), botao = $("[data-finalizar]", formCk);
            caixaTotal.hidden = total === 0;
            $("[data-total-qtd]", formCk).textContent = total === 1 ? "1 ingresso" : `${total} ingressos`;
            $("[data-total-valor]", formCk).textContent = valor <= 0 ? "Gratuito" : fmt(valor);
            botao.disabled = total === 0;
            botao.textContent = total === 0 ? "Escolha os ingressos"
                : valor > 0 ? `Comprar ${total === 1 ? "1 ingresso" : total + " ingressos"} · ${fmt(valor)}`
                : total === 1 ? "Confirmar inscrição" : `Confirmar ${total} inscrições`;
        }
        recalcularCheckout = atualizar;

        linhas.forEach(o => {
            const tipo = +o.dataset.tipo, meia = o.dataset.meia === "1";
            $("[data-mais]", o)?.addEventListener("click", () => {
                lerTela();
                const novo = { tipo, meia, meu: !estado.some(p => p.meu) };
                estado.push(novo);
                estado.sort((a, b) => ordem(a) - ordem(b));
                desenhar();
                // Leva o foco para o novo participante (quando não é o próprio comprador).
                const f = $$(".participante", lista)[estado.indexOf(novo)];
                if (f && !$("[data-meu]", f).checked) $("[data-campo=nome]", f).focus({ preventScroll: true });
            });
            $("[data-menos]", o)?.addEventListener("click", () => {
                lerTela();
                // Remove de preferência um participante ainda em branco desta linha.
                const idx = estado.map((p, i) => ({ p, i })).filter(x => chave(x.p.tipo, x.p.meia) === chave(tipo, meia));
                if (!idx.length) return;
                const vazio = [...idx].reverse().find(x => !x.p.meu && !x.p.nome && !x.p.email && !x.p.telefone) || idx[idx.length - 1];
                const eraMeu = vazio.p.meu;
                estado.splice(vazio.i, 1);
                if (eraMeu && estado.length && !estado.some(p => p.meu)) estado[0].meu = true;
                desenhar();
            });
        });
        formCk.addEventListener("submit", () => lerTela());
        desenhar();
    }

    // ---------- Pedido: espera o Pix cair e recarrega a página ----------
    const esperaPix = $("[data-situacao]");
    if (esperaPix?.dataset.situacao) {
        let tentativas = 0;
        const conferir = async () => {
            if (document.hidden) return;
            tentativas++;
            try {
                const r = await (await fetch(esperaPix.dataset.situacao, { headers: { Accept: "application/json" } })).json();
                if (r.pago) { location.reload(); return; }
            } catch { }
        };
        // A cada 5 s nos primeiros 15 min; depois a cada 30 s.
        const ciclo = () => setTimeout(async () => { await conferir(); ciclo(); }, tentativas < 180 ? 5000 : 30000);
        ciclo();
    }

    // CPF: 000.000.000-00 enquanto digita.
    $$('input[name=cpf]').forEach(i => i.addEventListener("input", () => {
        const d = i.value.replace(/\D/g, "").slice(0, 11);
        i.value = d.replace(/^(\d{3})(\d)/, "$1.$2").replace(/^(\d{3})\.(\d{3})(\d)/, "$1.$2.$3").replace(/\.(\d{3})(\d)/, ".$1-$2");
    }));

    // ---------- Checkout: cartão de crédito (Silent Order Post da Cielo) ----------
    // O número, a validade e o CVV não têm "name": nunca são enviados ao Plateia.
    // O script da Cielo lê esses campos, manda direto para a Cielo e devolve um PaymentToken; só o token vem para o servidor.
    const formCartao = $("#form-cartao");
    if (formCartao) {
        const num = $("[data-numero]", formCartao), nome = $("[data-nome]", formCartao), val = $("[data-validade]", formCartao), cvv = $("[data-cvv]", formCartao);
        const erroCartao = $("[data-erro-cartao]", formCartao), botao = $("[data-pagar-cartao]", formCartao);
        const previa = $("[data-previa]");
        // Bandeiras aceitas pela Cielo (Elo e Hipercard antes de Visa/Master, porque alguns BINs se sobrepõem).
        const bandeiras = [
            ["Elo", /^(401178|401179|431274|438935|451416|457393|457631|457632|504175|506699|5067[0-7]\d|509\d{3}|627780|636297|636368|650\d{3}|6516[5-7]\d|6550[0-5]\d)/],
            ["Hipercard", /^(606282|3841)/],
            ["Amex", /^3[47]/],
            ["Diners", /^3(0[0-5]|[68])/],
            ["Discover", /^(6011|65|64[4-9])/],
            ["JCB", /^35(2[89]|[3-8])/],
            ["Visa", /^4/],
            ["Master", /^(5[1-5]|2(2[2-9]|[3-6]\d|7[01]|720))/]
        ];
        const bandeiraDe = d => (bandeiras.find(([, re]) => re.test(d)) || [""])[0];
        const luhn = d => { let s = 0; for (let i = 0; i < d.length; i++) { let n = +d[d.length - 1 - i]; if (i % 2) { n *= 2; if (n > 9) n -= 9; } s += n; } return d.length >= 13 && s % 10 === 0; };
        const mostrarErro = t => { erroCartao.textContent = t; erroCartao.hidden = !t; };

        num.addEventListener("input", () => {
            const d = num.value.replace(/\D/g, "").slice(0, 19);
            const b = bandeiraDe(d);
            num.value = b === "Amex" ? d.replace(/^(\d{4})(\d{0,6})(\d{0,5}).*/, (_, a, c, e) => [a, c, e].filter(Boolean).join(" ")) : d.replace(/(\d{4})(?=\d)/g, "$1 ");
            $("[data-bandeira-txt]", formCartao).textContent = b;
            cvv.maxLength = b === "Amex" ? 4 : 3;
            if (previa) {
                $("[data-previa-numero]", previa).textContent = (num.value || "•••• •••• •••• ••••");
                $("[data-previa-bandeira]", previa).textContent = b;
                previa.dataset.bandeira = b.toLowerCase();
            }
        });
        nome.addEventListener("input", () => { if (previa) $("[data-previa-nome]", previa).textContent = nome.value.toUpperCase() || "NOME COMO NO CARTÃO"; });
        val.addEventListener("input", () => {
            const d = val.value.replace(/\D/g, "").slice(0, 4);
            val.value = d.length > 2 ? `${d.slice(0, 2)}/${d.slice(2)}` : d;
            if (previa) $("[data-previa-validade]", previa).textContent = val.value || "MM/AA";
        });
        cvv.addEventListener("input", () => cvv.value = cvv.value.replace(/\D/g, "").slice(0, cvv.maxLength));

        // Carrega o script da Cielo só nesta página (e só quando o pagamento com cartão está configurado).
        if (formCartao.dataset.script) {
            const s = document.createElement("script");
            s.src = formCartao.dataset.script;
            s.async = true;
            s.onerror = () => mostrarErro("Não foi possível carregar o pagamento da Cielo. Atualize a página ou pague com Pix.");
            document.head.append(s);
        }

        formCartao.addEventListener("submit", ev => {
            if ($("[data-token-cartao]", formCartao).value) return; // já tem o token: envia ao Plateia
            ev.preventDefault();
            mostrarErro("");
            const d = num.value.replace(/\D/g, ""), b = bandeiraDe(d);
            const [mm, aa] = val.value.split("/");
            const agora = new Date(), ano = 2000 + +(aa || 0), mes = +(mm || 0);
            if (!b || !luhn(d)) return mostrarErro("Número do cartão inválido. Confira os dígitos.");
            if (!(mes >= 1 && mes <= 12) || ano < agora.getFullYear() || (ano === agora.getFullYear() && mes < agora.getMonth() + 1))
                return mostrarErro("Validade inválida ou vencida. Use o formato MM/AA.");
            if (cvv.value.length < (b === "Amex" ? 4 : 3)) return mostrarErro("Confira o código de segurança (CVV) no verso do cartão.");
            if (nome.value.trim().length < 3) return mostrarErro("Informe o nome como está impresso no cartão.");
            if (typeof window.bpSop_silentOrderPost !== "function") return mostrarErro("O pagamento da Cielo ainda está carregando. Tente de novo em instantes.");

            $("[data-validade-completa]", formCartao).value = `${String(mes).padStart(2, "0")}/${ano}`;
            $("[data-bandeira]", formCartao).value = b;
            nome.value = nome.value.toUpperCase();
            botao.disabled = true;
            botao.dataset.texto ??= botao.innerHTML;
            botao.textContent = "Processando pagamento…";
            const voltar = msg => { botao.disabled = false; botao.innerHTML = botao.dataset.texto; mostrarErro(msg); };
            window.bpSop_silentOrderPost({
                accessToken: formCartao.dataset.token,
                environment: formCartao.dataset.ambiente,
                language: "PT",
                enableTokenize: false,
                cvvRequired: true,
                onSuccess: r => {
                    if (!r || !r.PaymentToken) return voltar("A Cielo não confirmou os dados do cartão. Tente de novo.");
                    $("[data-token-cartao]", formCartao).value = r.PaymentToken;
                    botao.textContent = "Confirmando com o banco…";
                    formCartao.submit();
                },
                onError: () => voltar("Não foi possível validar o cartão na Cielo. Confira os dados e tente de novo."),
                onInvalid: () => voltar("Algum dado do cartão está inválido. Confira número, validade e CVV."),
                onBinNotFound: () => voltar("Não reconhecemos este cartão. Confira o número ou use outro cartão.")
            });
        });
    }

    // ---------- Página pública: cupom de desconto ----------
    const formIns = $("#form-inscricao");
    if (formIns && $("[data-aplicar-cupom]", formIns)) {
        const campo = $("input[name=cupom]", formIns), msg = $(".cupom-msg", formIns);
        const brl = new Intl.NumberFormat("pt-BR", { style: "currency", currency: "BRL" });
        const fmt = v => v <= 0 ? "Gratuito" : brl.format(v).replace(",00", "");
        const precos = $$(".preco[data-preco]", formIns);
        const original = () => { precos.forEach(p => p.textContent = fmt(+p.dataset.preco)); cupomAtual = null; recalcularCheckout(); };
        const aplicar = async () => {
            const codigo = campo.value.trim();
            original();
            msg.textContent = ""; msg.className = "cupom-msg";
            if (!codigo) return;
            try {
                const r = await (await fetch(`${formIns.dataset.cupomUrl}?codigo=${encodeURIComponent(codigo)}`)).json();
                msg.textContent = r.texto;
                msg.classList.add(r.valido ? "ok" : "erro");
                if (!r.valido) return;
                cupomAtual = { tipo: r.tipo, valor: +r.valor };
                recalcularCheckout();
                precos.forEach(p => {
                    const base = +p.dataset.preco;
                    if (base <= 0) return;
                    const desc = Math.min(base, r.tipo === "Percentual" ? base * r.valor / 100 : r.valor);
                    p.innerHTML = `<s>${fmt(base)}</s>${fmt(Math.round((base - desc) * 100) / 100)}`;
                });
            } catch { }
        };
        $("[data-aplicar-cupom]", formIns).addEventListener("click", aplicar);
        campo.addEventListener("keydown", e => { if (e.key === "Enter") { e.preventDefault(); aplicar(); } });
        if (campo.value) aplicar();
    }

    // ---------- Divulgação: canal personalizado ----------
    const canalLivre = $("#canal-livre");
    if (canalLivre) {
        const slug = t => t.normalize("NFD").replace(/[̀-ͯ]/g, "").toLowerCase().replace(/[^a-z0-9]+/g, "-").replace(/^-+|-+$/g, "").slice(0, 40);
        const montar = () => `${canalLivre.dataset.base}?origem=${slug($("input", canalLivre).value) || "…"}`;
        $("input", canalLivre).addEventListener("input", () => $("[data-saida]", canalLivre).textContent = montar());
        canalLivre.addEventListener("submit", async e => {
            e.preventDefault();
            if (!slug($("input", canalLivre).value)) { aviso("Digite o nome do canal.", true); return; }
            try { await navigator.clipboard.writeText(montar()); aviso("Link copiado."); } catch { prompt("Copie:", montar()); }
        });
    }

    // ---------- Divulgação: artes para stories e feed (desenhadas no navegador) ----------
    const artes = $("[data-arte]");
    if (artes) {
        const d = artes.dataset;
        const carregarQr = new Promise(ok => {
            const img = new Image();
            img.onload = () => ok(img); img.onerror = () => ok(null);
            img.src = d.qr;
        });
        const quebrar = (ctx, texto, largura) => {
            const linhas = []; let atual = "";
            for (const p of texto.split(/\s+/)) {
                const teste = atual ? atual + " " + p : p;
                if (ctx.measureText(teste).width > largura && atual) { linhas.push(atual); atual = p; } else atual = teste;
            }
            if (atual) linhas.push(atual);
            return linhas;
        };
        const desenhar = async canvas => {
            const W = canvas.width, H = canvas.height, stories = canvas.dataset.formato === "stories";
            const ctx = canvas.getContext("2d");
            const m = 84;
            ctx.fillStyle = d.cor; ctx.fillRect(0, 0, W, H);
            // plateia em arco ao fundo
            ctx.save(); ctx.globalAlpha = .16; ctx.fillStyle = d.tinta;
            const cx = W / 2, cy = H * (stories ? .64 : .78);
            for (let fila = 0; fila < 9; fila++) {
                const r = 260 + fila * 62, n = 14 + fila * 4;
                for (let k = 0; k <= n; k++) {
                    const a = Math.PI + (Math.PI * k / n);
                    ctx.beginPath(); ctx.arc(cx + Math.cos(a) * r, cy + Math.sin(a) * r * .62, 9, 0, 7); ctx.fill();
                }
            }
            ctx.restore();
            ctx.fillStyle = d.tinta;
            ctx.font = "600 34px 'JetBrains Mono', monospace";
            ctx.fillText(d.numero, m, m + 20);
            ctx.textAlign = "right"; ctx.fillText(d.data.split("·")[1]?.trim() ?? "", W - m, m + 20); ctx.textAlign = "left";
            // título: diminui a fonte até caber
            let tam = stories ? 168 : 150, linhas;
            do { ctx.font = `400 ${tam}px Anton, Impact, sans-serif`; linhas = quebrar(ctx, d.titulo.toUpperCase(), W - m * 2); tam -= 6; }
            while (linhas.length * tam * .98 > H * (stories ? .36 : .34) && tam > 60);
            let y = stories ? 330 : 200;
            for (const l of linhas) { y += tam * .98; ctx.fillText(l, m, y); }
            if (d.sub) {
                ctx.font = "500 40px 'Instrument Sans', sans-serif";
                for (const l of quebrar(ctx, d.sub, W - m * 2).slice(0, 2)) { y += 56; ctx.fillText(l, m, y + 20); }
            }
            // canhoto do ingresso, com recortes e picote
            const hTicket = stories ? 520 : 330, yT = H - hTicket - m;
            ctx.fillStyle = "#FFFFFF";
            ctx.beginPath(); ctx.roundRect(m, yT, W - m * 2, hTicket, 36); ctx.fill();
            ctx.fillStyle = d.cor;
            [m, W - m].forEach(x => { ctx.beginPath(); ctx.arc(x, yT + 120, 26, 0, 7); ctx.fill(); });
            ctx.strokeStyle = "#D9D1C7"; ctx.setLineDash([14, 12]); ctx.lineWidth = 4;
            ctx.beginPath(); ctx.moveTo(m + 40, yT + 120); ctx.lineTo(W - m - 40, yT + 120); ctx.stroke(); ctx.setLineDash([]);
            ctx.fillStyle = "#17130F";
            ctx.font = "700 46px 'Instrument Sans', sans-serif"; ctx.fillText(d.data.split("·")[0].trim(), m + 44, yT + 78);
            ctx.font = "500 36px 'Instrument Sans', sans-serif"; ctx.fillStyle = "#6B6259";
            ctx.fillText(d.local, m + 44, yT + 190);
            ctx.font = "700 44px 'JetBrains Mono', monospace"; ctx.fillStyle = "#17130F";
            ctx.fillText(d.preco, m + 44, yT + 260);
            if (stories) {
                ctx.font = "400 64px Anton, Impact, sans-serif"; ctx.fillText("INSCRIÇÕES ABERTAS", m + 44, yT + 380);
                ctx.font = "500 30px 'JetBrains Mono', monospace"; ctx.fillStyle = "#6B6259";
                ctx.fillText(d.link.length > 34 ? d.link.slice(0, 33) + "…" : d.link, m + 44, yT + 440);
            }
            const qr = await carregarQr;
            if (qr) { const q = stories ? 250 : 170; ctx.drawImage(qr, W - m - 44 - q, yT + (stories ? 190 : 140), q, q); }
        };
        (async () => {
            try { await Promise.all(["400 100px Anton", "500 40px 'Instrument Sans'", "700 40px 'Instrument Sans'", "600 30px 'JetBrains Mono'", "700 30px 'JetBrains Mono'"].map(f => document.fonts.load(f))); } catch { }
            for (const c of $$("canvas", artes)) await desenhar(c);
        })();
        artes.addEventListener("click", e => {
            const b = e.target.closest("[data-baixar]");
            if (!b) return;
            $(`canvas[data-formato=${b.dataset.baixar}]`, artes).toBlob(blob => {
                const a = document.createElement("a");
                a.href = URL.createObjectURL(blob); a.download = `divulgacao-${b.dataset.baixar}.png`; a.click();
                setTimeout(() => URL.revokeObjectURL(a.href), 2000);
            }, "image/png");
        });
    }

    // Compartilhar evento (menu nativo do celular, ou copia o link)
    document.addEventListener("click", async e => {
        const b = e.target.closest("[data-compartilhar]");
        if (!b) return;
        const url = b.dataset.compartilhar;
        if (navigator.share) { try { await navigator.share({ title: b.dataset.titulo, url }); return; } catch { return; } }
        try { await navigator.clipboard.writeText(url); aviso("Link do evento copiado."); } catch { prompt("Copie:", url); }
    });

    // ---------- Formulário do evento: espaços ligados aos ingressos ----------
    const listaEsp = $("#espacos");
    if (listaEsp) {
        let novos = 1000;
        const escH = t => String(t).replace(/[&<>"']/g, c => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c]));
        const capTotal = $("input[name=capacidade]");
        const linhasEsp = () => $$(".espaco-linha:not(.removido)", listaEsp);
        const sincronizar = () => {
            const ops = linhasEsp().map(l => ({
                chave: $("input[name=espaco_chave]", l).value,
                nome: $("[data-nome-espaco]", l).value.trim() || "Espaço sem nome",
                cap: parseInt($("[data-cap-espaco]", l).value, 10) || 0
            }));
            $$("select[name=tipo_espaco]").forEach(sel => {
                const atual = sel.value || sel.dataset.espacoAtual;
                // Sem espaços, um valor vazio continua sendo enviado para não desalinhar as listas do formulário.
                sel.innerHTML = ops.length ? ops.map(o => `<option value="${escH(o.chave)}">${escH(o.nome)}</option>`).join("") : `<option value=""></option>`;
                if (ops.some(o => o.chave === atual)) sel.value = atual;
                sel.dataset.espacoAtual = sel.value;
                sel.closest(".campo").hidden = ops.length === 0;
            });
            const soma = ops.reduce((a, o) => a + o.cap, 0);
            $("#soma-espacos").textContent = ops.length ? `Lotação total: ${soma.toLocaleString("pt-BR")} lugares` : "";
            $(".capacidade-total").hidden = ops.length > 0;
            if (ops.length && soma > 0) capTotal.value = soma;
        };
        $("#add-espaco").addEventListener("click", () => {
            const n = $("#modelo-espaco").content.firstElementChild.cloneNode(true);
            $("input[name=espaco_chave]", n).value = "n" + (novos++);
            listaEsp.append(n);
            sincronizar();
            $("[data-nome-espaco]", n).focus();
        });
        listaEsp.addEventListener("click", e => {
            const b = e.target.closest("[data-remover-espaco]");
            if (!b) return;
            const l = b.closest(".espaco-linha");
            if (!$("input[name=espaco_id]", l).value) l.remove();
            else {
                $("input[name=espaco_remover]", l).value = "1";
                $$("[required]", l).forEach(i => i.required = false);
                l.classList.add("removido");
            }
            sincronizar();
        });
        listaEsp.addEventListener("input", sincronizar);
        $("#add-tipo")?.addEventListener("click", () => setTimeout(sincronizar));
        sincronizar();

        // Prévia da capa escolhida e do formato
        $("[data-previa-capa]")?.addEventListener("change", e => {
            const f = e.target.files?.[0];
            if (!f) return;
            const cartaz = $(".previa .cartaz");
            let img = $(".previa-img", cartaz);
            if (!img) { img = document.createElement("img"); img.className = "previa-img"; img.alt = ""; cartaz.prepend(img); }
            img.src = URL.createObjectURL(f);
        });
        $$("input[name=formato]").forEach(r => r.addEventListener("change", () => {
            if (r.checked && r.value === "Online") $("#previa-local").textContent = "Online";
        }));
    }

    // ---------- Menu da vitrine: um aberto por vez, fecha ao clicar fora ou com Esc ----------
    const drops = $$("details.drop");
    if (drops.length) {
        drops.forEach(d => d.addEventListener("toggle", () => {
            if (d.open) drops.forEach(o => { if (o !== d) o.open = false; });
        }));
        document.addEventListener("click", e => drops.forEach(d => { if (d.open && !d.contains(e.target)) d.open = false; }));
        document.addEventListener("keydown", e => {
            if (e.key !== "Escape") return;
            const aberto = drops.find(d => d.open);
            if (aberto) { aberto.open = false; $("summary", aberto).focus(); }
        });
    }

    // ---------- Portaria ----------
    const portaria = $("#form-checkin");
    if (portaria) {
        const campo = $("input[name=codigo]", portaria);
        const caixa = $("#resultado");
        const contador = $("#presentes");
        let ultimo = "", ocupado = false;

        const bip = ok => {
            try {
                const ctx = bip.ctx ??= new AudioContext();
                const o = ctx.createOscillator(), g = ctx.createGain();
                o.frequency.value = ok ? 880 : 220; o.type = ok ? "sine" : "square";
                g.gain.setValueAtTime(.12, ctx.currentTime); g.gain.exponentialRampToValueAtTime(.001, ctx.currentTime + (ok ? .18 : .35));
                o.connect(g).connect(ctx.destination); o.start(); o.stop(ctx.currentTime + .4);
            } catch { }
            navigator.vibrate?.(ok ? 40 : [80, 60, 80]);
        };

        const titulos = { Liberado: "Entrada liberada", JaEntrou: "Já entrou", PagamentoPendente: "Pagamento pendente", Cancelada: "Inscrição cancelada", NaoEncontrado: "Não encontrado" };
        const esc = s => String(s ?? "").replace(/[&<>"']/g, c => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c]));

        const mostrar = (r, codigo) => {
            const i = r.inscricao;
            caixa.className = `bilhete resultado ${r.resultado}`;
            void caixa.offsetWidth;
            caixa.classList.add("chegou");
            let corpo = "";
            if (!i) corpo = `<p class="suave">Nenhum ingresso deste evento com o código <b class="mono">${esc(codigo)}</b>. Confira as letras ou busque pelo nome na lista de participantes.</p>`;
            else {
                corpo = `<div class="pessoa" style="display:flex;gap:14px;align-items:center"><span class="av" style="width:52px;height:52px;font-size:16px">${esc(i.iniciais)}</span>
                    <div><h3>${esc(i.nome)}</h3><span class="suave">${esc(i.tipo)} · <span class="mono">${esc(i.codigo)}</span></span></div></div>`;
                if (r.resultado === "JaEntrou") corpo += `<p>Entrou às <b class="mono">${esc(i.hora)}</b>. Pode ser o mesmo ingresso sendo usado duas vezes.</p>`;
                if (r.resultado === "Cancelada") corpo += `<p>Esta inscrição foi cancelada e não dá direito à entrada.</p>`;
                if (r.resultado === "PagamentoPendente") corpo += `<p>Falta pagar <b class="mono">${esc(i.valor)}</b>. Receba agora e libere a entrada, ou oriente a pessoa.</p>
                    <button class="btn pri" type="button" data-liberar="${esc(i.codigo)}">Recebi o pagamento e liberar</button>`;
                if (r.resultado === "Liberado") corpo += `<span class="carimbo batendo" style="justify-self:start;font-size:22px">PRESENTE<small>${esc(i.hora)}</small></span>`;
            }
            caixa.innerHTML = `<div class="topo-r"><span>${titulos[r.resultado]}</span></div><div class="corpo-r">${corpo}</div>`;
            if (contador) { contador.dataset.contar = r.presentes; contar(contador); }
            bip(r.resultado === "Liberado");
            if (r.resultado === "Liberado" && i) {
                const lista = $("#recentes");
                if (lista) {
                    $(".vazio", lista)?.remove();
                    const li = document.createElement("li");
                    li.className = "novo";
                    li.innerHTML = `<span class="ic Entrou">✓</span><span>${esc(i.nome)}</span><time>${esc(i.hora)}</time>`;
                    lista.prepend(li);
                }
            }
        };

        const checar = async (codigo, liberar = false) => {
            if (ocupado || !codigo) return;
            ocupado = true;
            try {
                const r = await enviar(portaria.action, { codigo, liberar });
                mostrar(r, codigo);
            } catch (err) { aviso(err.message, true); }
            finally { ocupado = false; campo.value = ""; campo.focus(); }
        };

        portaria.addEventListener("submit", e => { e.preventDefault(); checar(campo.value.trim()); });
        caixa.addEventListener("click", e => {
            const b = e.target.closest("[data-liberar]");
            if (b) checar(b.dataset.liberar, true);
        });

        // Leitura do QR pela câmera (navegadores com BarcodeDetector, ex.: Chrome no Android).
        const btnCam = $("#btn-camera");
        if (btnCam && "BarcodeDetector" in window) {
            btnCam.hidden = false;
            let stream, timer;
            btnCam.addEventListener("click", async () => {
                const box = $(".camera");
                if (stream) { stream.getTracks().forEach(t => t.stop()); stream = null; clearInterval(timer); box.classList.remove("on"); btnCam.textContent = "Ler QR com a câmera"; return; }
                try {
                    stream = await navigator.mediaDevices.getUserMedia({ video: { facingMode: "environment" } });
                    const video = $("video", box);
                    video.srcObject = stream; await video.play();
                    box.classList.add("on"); btnCam.textContent = "Desligar câmera";
                    const det = new BarcodeDetector({ formats: ["qr_code"] });
                    timer = setInterval(async () => {
                        if (ocupado) return;
                        const [c] = await det.detect(video).catch(() => []);
                        if (c && c.rawValue !== ultimo) { ultimo = c.rawValue; checar(c.rawValue); setTimeout(() => ultimo = "", 4000); }
                    }, 350);
                } catch { aviso("Não foi possível abrir a câmera. Digite o código.", true); }
            });
        }
    }
})();
