using System.Globalization;
using HtmlAgilityPack;

namespace FIIsCLI;

/// <summary>
/// Lê e interpreta a tabela de FIIs do Fundamentus (interface "classic").
/// </summary>
internal static class FundamentusScraper
{
    public const string Url = "https://fundamentus.com.br/fii_resultado.php?&interface=classic";

    /// <summary>Ordem das colunas da tabela, como publicada pelo Fundamentus.</summary>
    private static class Col
    {
        public const int Papel = 0;           // contém o <a> (ticker) e o <span title=...> (nome)
        public const int Segmento = 1;
        public const int Cotacao = 2;
        public const int FfoYield = 3;
        public const int DividendYield = 4;
        public const int PVP = 5;
        public const int ValorDeMercado = 6;
        public const int Liquidez = 7;
        public const int QtdDeImoveis = 8;
        public const int PrecoM2 = 9;
        public const int AluguelM2 = 10;
        public const int CapRate = 11;
        public const int VacanciaMedia = 12;
        public const int Endereco = 13;

        public const int Count = 14;
    }

    /// <summary>
    /// Baixa a página e devolve um <see cref="FII"/> por linha válida da tabela.
    /// Linhas com formato inesperado são puladas e reportadas em <paramref name="skipped"/>,
    /// em vez de derrubar a aplicação inteira.
    /// </summary>
    public static List<FII> Load(out int skipped)
    {
        HtmlDocument document = new HtmlWeb().Load(Url, "GET");
        return Parse(document, out skipped);
    }

    public static List<FII> Parse(HtmlDocument document, out int skipped)
    {
        HtmlNode? tbody = document.DocumentNode.SelectSingleNode("//tbody")
            ?? throw new InvalidOperationException(
                "Não encontrei o <tbody> da tabela — o layout do Fundamentus deve ter mudado.");

        List<FII> fiis_rows = new();
        skipped = 0;

        foreach (HtmlNode row in tbody.SelectNodes("./tr") ?? Enumerable.Empty<HtmlNode>())
        {
            FII? fii = ParseRow(row);
            if (fii is null)
            {
                skipped++;
                continue;
            }

            fiis_rows.Add(fii);
        }

        return fiis_rows;
    }

    private static FII? ParseRow(HtmlNode row)
    {
        // Materializa as células uma única vez: o código original chamava
        // Descendants("td").ElementAt(n) catorze vezes por linha.
        HtmlNode[] cells = row.SelectNodes("./td")?.ToArray() ?? Array.Empty<HtmlNode>();
        if (cells.Length < Col.Count)
        {
            return null;
        }

        HtmlNode papel = cells[Col.Papel];

        // O ticker é a chave da linha: sem ele a linha não serve para nada.
        string ticker = Text(papel.SelectSingleNode(".//a"));
        if (ticker.Length == 0)
        {
            return null;
        }

        // O nome vem do atributo title do <span> — buscado pelo nome, não por
        // Attributes[1], que quebra a qualquer reordenação de atributos.
        HtmlNode? span = papel.SelectSingleNode(".//span");
        string nome = span?.GetAttributeValue("title", null) is { Length: > 0 } title
            ? HtmlEntity.DeEntitize(title).Trim()
            : ticker;

        return new FII(
            Nome: nome,
            Ticker: ticker,
            Segmento: Text(cells[Col.Segmento]),
            Cotaçao: ParseDecimal(Text(cells[Col.Cotacao])),
            FfoYeld: ParseDecimal(Text(cells[Col.FfoYield])),
            DividendYield: ParseDecimal(Text(cells[Col.DividendYield])),
            P_VP: ParseDecimal(Text(cells[Col.PVP])),
            ValorDeMercado: ParseDecimal(Text(cells[Col.ValorDeMercado])),
            Liquidez: ParseDecimal(Text(cells[Col.Liquidez])),
            QtdDeImoveis: ParseInt(Text(cells[Col.QtdDeImoveis])),
            PrecoM2: ParseDecimal(Text(cells[Col.PrecoM2])),
            AluguelM2: ParseDecimal(Text(cells[Col.AluguelM2])),
            CapRate: ParseDecimal(Text(cells[Col.CapRate])),
            VacanciaMedia: ParseDecimal(Text(cells[Col.VacanciaMedia])),
            Endereco: Text(cells[Col.Endereco]));
    }

    private static string Text(HtmlNode? node) =>
        node is null ? string.Empty : HtmlEntity.DeEntitize(node.InnerText).Trim();

    /// <summary>
    /// Converte o formato brasileiro do Fundamentus ("1.234,56", "8,50%", "-0,30%")
    /// para decimal. Percentuais viram fração (8,50% =&gt; 0.085).
    /// </summary>
    internal static decimal ParseDecimal(string value)
    {
        int signal = value.Contains('-') ? -1 : 1;
        decimal scale = value.Contains('%') ? 0.01m : 1m;

        string normalized = value
            .Replace("-", "")
            .Replace("%", "")
            .Replace("R$", "")
            .Replace(".", "")   // separador de milhar
            .Replace(",", ".")  // separador decimal
            .Trim();

        // Cultura invariante: depois da normalização o separador decimal é '.',
        // e o original dependia da cultura da máquina (quebrava em pt-BR).
        return decimal.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal result)
            ? result * signal * scale
            : 0m;
    }

    /// <summary>Converte inteiros com separador de milhar; 0 quando a célula vier vazia ou ilegível.</summary>
    internal static int ParseInt(string value)
    {
        string normalized = value.Replace(".", "").Replace(",", "").Trim();

        return int.TryParse(normalized, NumberStyles.Integer, CultureInfo.InvariantCulture, out int result)
            ? result
            : 0;
    }
}
