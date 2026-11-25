using System.Globalization;

namespace FIIsCLI;

internal static class Program
{
    /// <summary>Estratégia de seleção aplicada ao universo de fundos.</summary>
    private enum Strategy
    {
        /// <summary>Equilibrada (padrão) — ver <see cref="ApplyBalancedStrategy"/>.</summary>
        Balanced,

        /// <summary>S-Rank, como no binário original — ver <see cref="ApplyS_RankStrategy"/>.</summary>
        SRank
    }

    // ---- Critérios do S-Rank (recuperados do binário original) --------------
    private const decimal MinDividendYield = 0.04m;
    private const decimal MinPvp = 0.04m;
    private const decimal MaxPvp = 1.2m;
    private const decimal MinValorDeMercado = 500_000_000m;
    private const decimal MaxVacanciaMedia = 0.3m;
    private const decimal MinLiquidez = 1_000_000m;

    // ---- Critérios da estratégia Equilibrada -------------------------------
    private const decimal MinLiquidezDiaria = 2_000_000m;
    private const decimal MinDy = 0.07m;
    private const decimal MaxDy = 0.20m;
    private const decimal MaxDyOverFfo = 1.5m;
    private const decimal MinPvpTijolo = 0.55m;
    private const decimal MinPvpPapel = 0.80m;
    private const decimal MaxPvpBalanced = 1.05m;
    private const decimal MaxVacanciaTijolo = 0.15m;
    private const int MinImoveis = 3;

    private const int DefaultLimit = 30;

    private static readonly string[] NordTickers =
    {
        "BRCR11", "HGLG11", "JSRE11", "HGRE11", "RECT11", "KNRI11", "HSML11",
        "MGFF11", "GTWR11", "HGBS11", "BRCO11", "GGRC11", "RBRF11"
    };

    private static readonly string[] MyTickers =
    {
        "VRTA11", "RBRY11", "RBRF11", "VILG11", "HGPO11", "HSML11", "HGRU11",
        "HGRE11", "HSLG11", "DEVA11", "KNSC11", "JSRE11", "VGIR11", "BTLG11", "RVBI11"
    };

    private static readonly CultureInfo Ptbr = CultureInfo.GetCultureInfo("pt-BR");

    private static int Main(string[] args)
    {
        Options options;
        try
        {
            options = Options.Parse(args);
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine(ex.Message);
            Console.Error.WriteLine();
            Options.PrintUsage(Console.Error);
            return 2;
        }

        if (options.ShowHelp)
        {
            Options.PrintUsage(Console.Out);
            return 0;
        }

        List<FII> fiis;
        int skipped;
        try
        {
            Console.Error.WriteLine("Lendo {0} ...", FundamentusScraper.Url);
            fiis = FundamentusScraper.Load(out skipped);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            Console.Error.WriteLine("Falha ao ler o Fundamentus: {0}", ex.Message);
            return 1;
        }

        if (skipped > 0)
        {
            Console.Error.WriteLine("Aviso: {0} linha(s) com formato inesperado foram ignoradas.", skipped);
        }

        IEnumerable<FII> selected = options.Tickers is { Length: > 0 } tickers
            ? FilterTickers(fiis, tickers)
            : options.Strategy switch
            {
                Strategy.SRank => ApplyS_RankStrategy(fiis, options.Limit),
                _ => ApplyBalancedStrategy(fiis, options.Limit)
            };

        List<FII> ranking = selected.ToList();

        ClearScreen();

        foreach (FII fii in ranking)
        {
            Print(fii, options.Strategy);
        }

        int papel = ranking.Count(IsPaperFund);
        Console.Error.WriteLine(
            "{0} de {1} fundos selecionados ({2} tijolo, {3} papel) — estratégia {4}.",
            ranking.Count, fiis.Count, ranking.Count - papel, papel, options.Strategy);

        Pause(options);
        return 0;
    }

    /// <summary>Fundos de uma lista fixa de tickers, do menor para o maior P/VP.</summary>
    private static IEnumerable<FII> FilterTickers(List<FII> fiis, string[] myTickers)
    {
        HashSet<string> wanted = new(myTickers, StringComparer.OrdinalIgnoreCase);

        return from x in fiis
               where wanted.Contains(x.Ticker)
               orderby x.P_VP
               select x;
    }

    /// <summary>Estratégia S-Rank: filtros fundamentalistas, ordenado por P/VP crescente.</summary>
    private static IEnumerable<FII> ApplyS_RankStrategy(List<FII> fiis, int limit)
    {
        return (from x in fiis
                where x.DividendYield > MinDividendYield
                where x.P_VP > MinPvp && x.P_VP < MaxPvp
                where x.ValorDeMercado > MinValorDeMercado
                where x.VacanciaMedia < MaxVacanciaMedia
                where x.Liquidez > MinLiquidez
                orderby x.P_VP
                select x).Take(limit);
    }

    /// <summary>
    /// Estratégia Equilibrada. Parte dos mesmos dados do S-Rank e corrige três
    /// pontos cegos dele:
    /// <list type="number">
    /// <item>o P/VP ganha um piso, porque desconto extremo em FII costuma ser
    /// sintoma e não oportunidade — o S-Rank justamente ordena por ele e coloca
    /// o caso mais problemático em primeiro lugar;</item>
    /// <item>fundo de papel e de tijolo são avaliados com réguas diferentes,
    /// já que P/VP e vacância não significam a mesma coisa nos dois;</item>
    /// <item>a distribuição precisa estar coberta pelo resultado operacional,
    /// o que separa yield recorrente de yield inflado por evento não recorrente.</item>
    /// </list>
    /// A ordenação é por soma de posições (DY e P/VP pesam igual), em vez de um
    /// único eixo.
    /// </summary>
    private static IEnumerable<FII> ApplyBalancedStrategy(List<FII> fiis, int limit)
    {
        List<FII> eligible = fiis.Where(IsEligible).ToList();

        // Soma de rankings ao estilo "magic formula": barato e rentável pesam o
        // mesmo, e usar posição em vez do valor bruto imuniza contra o outlier
        // que dominaria uma média ponderada.
        Dictionary<FII, int> byYield = Rank(eligible, x => -x.DividendYield);
        Dictionary<FII, int> byPrice = Rank(eligible, x => x.P_VP);

        return eligible
            .OrderBy(x => byYield[x] + byPrice[x])
            .ThenByDescending(x => x.Liquidez)
            .Take(limit);
    }

    private static bool IsEligible(FII fii)
    {
        // Liquidez é a restrição que mais aperta (de 576 fundos, ~70 passam) e a
        // que mais dói na prática: sem giro não há como entrar nem sair.
        if (fii.Liquidez <= MinLiquidezDiaria || fii.ValorDeMercado <= MinValorDeMercado)
        {
            return false;
        }

        // Piso acima do CDI para um veículo de renda; teto para cortar o yield
        // que só existe porque o fundo devolveu capital ou vendeu ativo.
        if (fii.DividendYield < MinDy || fii.DividendYield > MaxDy)
        {
            return false;
        }

        if (fii.FfoYeld <= 0 || fii.DividendYield > fii.FfoYeld * MaxDyOverFfo)
        {
            return false;
        }

        return IsPaperFund(fii)
            ? fii.P_VP >= MinPvpPapel && fii.P_VP <= MaxPvpBalanced
            : fii.P_VP >= MinPvpTijolo
              && fii.P_VP <= MaxPvpBalanced
              && fii.VacanciaMedia < MaxVacanciaTijolo
              && fii.QtdDeImoveis >= MinImoveis;
    }

    /// <summary>
    /// Fundo sem carteira física reportada — na prática, fundo de papel.
    /// Heurística: a coluna Segmento não serve para isso ("Multicategoria" e
    /// "Outros" concentram metade da tabela, e há fundo de papel rotulado como
    /// "Logística"), então o sinal usado é a ausência de imóveis.
    /// </summary>
    private static bool IsPaperFund(FII fii) => fii.QtdDeImoveis == 0;

    /// <summary>Posição de cada fundo (1 = melhor) segundo <paramref name="key"/>.</summary>
    private static Dictionary<FII, int> Rank(List<FII> fiis, Func<FII, decimal> key)
    {
        Dictionary<FII, int> ranks = new(fiis.Count, ReferenceEqualityComparer.Instance);
        int position = 1;

        foreach (FII fii in fiis.OrderBy(key))
        {
            ranks[fii] = position++;
        }

        return ranks;
    }

    /// <summary>Quais critérios da estratégia ativa o fundo atende.</summary>
    private readonly record struct Checks(bool Dy, bool Pvp, bool Valor, bool Liquidez, bool Vacancia);

    private static Checks Check(FII fii, Strategy strategy) => strategy switch
    {
        Strategy.SRank => new Checks(
            Dy: fii.DividendYield > MinDividendYield,
            Pvp: fii.P_VP > MinPvp && fii.P_VP < MaxPvp,
            Valor: fii.ValorDeMercado > MinValorDeMercado,
            Liquidez: fii.Liquidez > MinLiquidez,
            Vacancia: fii.VacanciaMedia < MaxVacanciaMedia),

        _ => new Checks(
            Dy: fii.DividendYield >= MinDy && fii.DividendYield <= MaxDy,
            Pvp: IsPaperFund(fii)
                ? fii.P_VP >= MinPvpPapel && fii.P_VP <= MaxPvpBalanced
                : fii.P_VP >= MinPvpTijolo && fii.P_VP <= MaxPvpBalanced,
            Valor: fii.ValorDeMercado > MinValorDeMercado,
            Liquidez: fii.Liquidez > MinLiquidezDiaria,
            Vacancia: IsPaperFund(fii) || fii.VacanciaMedia < MaxVacanciaTijolo)
    };

    private static void Print(FII fii, Strategy strategy)
    {
        Checks checks = Check(fii, strategy);

        Console.WriteLine("##################################################");
        Console.WriteLine(fii.Nome);
        Console.WriteLine("##################################################");
        Console.WriteLine(
            "Ticker:. . . . . . .. . . {0}\n" +
            "Segmento: . . . . . . . . {1}\n" +
            "Cotação:. . . . . . . . . {2}\n" +
            "FFO Yield:. . . . . . . . {3}\n" +
            "Dividend Yield: . . . . . {4}\n" +
            "P/VP: . . . . . . . . . . {5}\n" +
            "Valor de Mercado: . . . . {6}\n" +
            "Liquidez: . . . . . . . . {7}\n" +
            "Qtd de imóveis: . . . . . {8}\n" +
            "Preço do m2:. . . . . . . {9}\n" +
            "Aluguel por m2: . . . . . {10}\n" +
            "Cap Rate: . . . . . . . . {11}\n" +
            "Vacância Média: . . . . . {12}\n" +
            "Endereço: . . . . . . . . {13}\n",
            fii.Ticker,
            $"{fii.Segmento} ({(IsPaperFund(fii) ? "papel" : "tijolo")})",
            Money(fii.Cotaçao),
            Percent(fii.FfoYeld),
            Mark(Percent(fii.DividendYield), checks.Dy),
            Mark(fii.P_VP.ToString("N2", Ptbr), checks.Pvp),
            Mark(Money(fii.ValorDeMercado), checks.Valor),
            Mark(Money(fii.Liquidez), checks.Liquidez),
            fii.QtdDeImoveis,
            Money(fii.PrecoM2),
            Money(fii.AluguelM2),
            Percent(fii.CapRate),
            Mark(Percent(fii.VacanciaMedia), checks.Vacancia),
            fii.Endereco);
    }

    /// <summary>Marca com "[!]" o valor que não atende ao critério da estratégia ativa.</summary>
    private static string Mark(string formatted, bool withinCriteria) =>
        withinCriteria ? formatted : formatted + "[!]";

    private static string Percent(decimal fraction) => fraction.ToString("P2", Ptbr);

    private static string Money(decimal value) => value.ToString("N2", Ptbr);

    private static void ClearScreen()
    {
        if (Console.IsOutputRedirected)
        {
            return;
        }

        try
        {
            Console.Clear();
        }
        catch (IOException)
        {
            // Terminal sem suporte a limpeza — segue imprimindo normalmente.
        }
    }

    private static void Pause(Options options)
    {
        if (options.NoPause || Console.IsInputRedirected || Console.IsOutputRedirected)
        {
            return;
        }

        Console.WriteLine("Pressione qualquer tecla para sair...");
        Console.ReadKey(intercept: true);
    }

    private sealed record Options(string[]? Tickers, int Limit, Strategy Strategy, bool NoPause, bool ShowHelp)
    {
        public static Options Parse(string[] args)
        {
            string[]? tickers = null;
            int limit = DefaultLimit;
            Strategy strategy = Strategy.Balanced;
            bool noPause = false;
            bool showHelp = false;

            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--limit" or "-l":
                        string rawLimit = Next(args, ref i, "--limit");
                        if (!int.TryParse(rawLimit, out limit) || limit <= 0)
                        {
                            throw new ArgumentException($"Limite inválido: '{rawLimit}'.");
                        }

                        break;

                    case "--srank":
                        strategy = Strategy.SRank;
                        break;

                    case "--equilibrada":
                        strategy = Strategy.Balanced;
                        break;

                    case "--tickers" or "-t":
                        tickers = Next(args, ref i, "--tickers")
                            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                        break;

                    case "--nord":
                        tickers = NordTickers;
                        break;

                    case "--meus":
                        tickers = MyTickers;
                        break;

                    case "--no-pause":
                        noPause = true;
                        break;

                    case "--help" or "-h" or "-?":
                        showHelp = true;
                        break;

                    default:
                        throw new ArgumentException($"Argumento desconhecido: '{args[i]}'.");
                }
            }

            return new Options(tickers, limit, strategy, noPause, showHelp);
        }

        private static string Next(string[] args, ref int i, string option)
        {
            if (i + 1 >= args.Length)
            {
                throw new ArgumentException($"A opção {option} exige um valor.");
            }

            return args[++i];
        }

        public static void PrintUsage(TextWriter writer)
        {
            writer.WriteLine("FIIsCLI — ranking de fundos imobiliários a partir do Fundamentus.");
            writer.WriteLine();
            writer.WriteLine("Uso: FIIsCLI [opções]");
            writer.WriteLine();
            writer.WriteLine("  -l, --limit <n>        Quantidade máxima de fundos no ranking (padrão: 30).");
            writer.WriteLine("      --equilibrada      Estratégia Equilibrada (padrão).");
            writer.WriteLine("      --srank            Estratégia S-Rank, como no binário original.");
            writer.WriteLine("  -t, --tickers <lista>  Mostra apenas estes tickers, separados por vírgula.");
            writer.WriteLine("      --nord             Usa a carteira NordTickers.");
            writer.WriteLine("      --meus             Usa a carteira MyTickers.");
            writer.WriteLine("      --no-pause         Não espera uma tecla ao final.");
            writer.WriteLine("  -h, --help             Mostra esta ajuda.");
            writer.WriteLine();
            writer.WriteLine("Equilibrada: liquidez > 2 mi, valor de mercado > 500 mi, 7% <= DY <= 20%,");
            writer.WriteLine("  DY coberto pelo FFO (<= 1,5x) e P/VP entre 0,80-1,05 (papel) ou 0,55-1,05");
            writer.WriteLine("  (tijolo, com vacância < 15% e >= 3 imóveis).");
            writer.WriteLine("  Ordena por soma das posições em DY e P/VP, desempate pela liquidez.");
            writer.WriteLine();
            writer.WriteLine("S-Rank: DY > 4%, 0,04 < P/VP < 1,2, valor de mercado > 500 mi,");
            writer.WriteLine("  vacância < 30% e liquidez > 1 mi, ordenado por P/VP crescente.");
            writer.WriteLine();
            writer.WriteLine("Valores fora dos critérios da estratégia ativa recebem \"[!]\".");
        }
    }
}
