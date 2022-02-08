using HtmlAgilityPack;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FIIsCLI
{
  class Program
  {
    static void Main(string[] args)
    {
      var fiis = new List<FII>();

      var url = "https://fundamentus.com.br/fii_resultado.php?&interface=classic";      

      var doc = new HtmlWeb().Load(url, "GET");      

      var fiis_rows = doc.DocumentNode.SelectSingleNode("//tbody").Descendants("tr");
      foreach (var row in fiis_rows)
      {
        fiis.Add(new FII(
          row.Descendants("td").First().Descendants("span").First().Attributes[1].Value,
          row.Descendants("td").First().Descendants("a").First().InnerHtml,
          row.Descendants("td").ElementAt(1).InnerHtml,
          FormatDecimal(row.Descendants("td").ElementAt(2).InnerHtml),
          FormatDecimal(row.Descendants("td").ElementAt(3).InnerHtml),
          FormatDecimal(row.Descendants("td").ElementAt(4).InnerHtml),
          FormatDecimal(row.Descendants("td").ElementAt(5).InnerHtml),
          FormatDecimal(row.Descendants("td").ElementAt(6).InnerHtml),
          FormatDecimal(row.Descendants("td").ElementAt(7).InnerHtml),
          int.Parse(row.Descendants("td").ElementAt(8).InnerHtml.Replace(',', '.')),
          FormatDecimal(row.Descendants("td").ElementAt(9).InnerHtml),
          FormatDecimal(row.Descendants("td").ElementAt(10).InnerHtml),
          FormatDecimal(row.Descendants("td").ElementAt(11).InnerHtml),
          FormatDecimal(row.Descendants("td").ElementAt(12).InnerHtml),
          row.Descendants("td").ElementAt(13).InnerHtml));
      }

      fiis = ApplyS_RankStrategy(fiis).ToList();
      Console.Clear();

      foreach (var fii in fiis)
      {
        Console.WriteLine(fii.Nome);
        Console.WriteLine(
            "Papel:. . . . . . . . . . {0}\n" +
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
            fii.Ticker, fii.Segmento, fii.Cotaçao, fii.FfoYeld, fii.DividendYield,
            fii.P_VP, fii.ValorDeMercado, fii.Liquidez, fii.QtdDeImoveis, fii.PrecoM2,
            fii.AluguelM2, fii.CapRate, fii.VacanciaMedia, fii.Endereco);
      }      

      string text = doc.DocumentNode.InnerText.Replace(Environment.NewLine, "");
      Console.WriteLine(text);
      Console.ReadKey();
    }

    /*
    private static IEnumerable<FII> ApplyPrimoStrategy(List<FII> fiis)
    {
      IEnumerable<FII> ranking = fiis
        .Where(x => x.DividendYield > 0.04m)
        .Where(x => x.P_VP > 0.04m && x.P_VP < 1.2m)
        .Where(x => x.ValorDeMercado > 500000000m)
        .Where(x => x.VacanciaMedia < 0.3m)
        .Where(x => x.Liquidez > 1000000m)
        .OrderByDescending(x => x.DividendYield)
        .Take(15);

      return ranking;
    }
    */
    private static IEnumerable<FII> ApplyS_RankStrategy(List<FII> fiis)
    {
      IEnumerable<FII> ranking = fiis
        .Where(x => x.DividendYield > 0.04m)
        .Where(x => x.P_VP > 0.04m && x.P_VP < 1.2m)
        .Where(x => x.ValorDeMercado > 500000000m)
        .Where(x => x.VacanciaMedia < 0.3m)
        .Where(x => x.Liquidez > 1000000m)
        .OrderBy(x => x.P_VP)
        .Take(30);

      return ranking;
    }

    private static decimal FormatDecimal(string value)
    {
      //verifico se é negativo e tento retirar o sinal
      int signal = value.Contains("-") ? -1 : 1;
      value = value.Replace("-", "");

      //verifico se é percentual e retiro o símbolo
      decimal pctg = value.Contains("%") ? 0.01m : 1;
      value = value.Replace("%", "");

      //retiro os pontos e depois troco virgulas por pontos
      value = value.Replace(".", "").Replace(",", ".");

      decimal.TryParse(value, out decimal result);
      return result * signal * pctg;
    }
  }

  public record FII(
    string Nome,
    string Ticker,
    string Segmento,
    decimal Cotaçao,
    decimal FfoYeld,
    decimal DividendYield,
    decimal P_VP,
    decimal ValorDeMercado,
    decimal Liquidez,
    int QtdDeImoveis,
    decimal PrecoM2,
    decimal AluguelM2,
    decimal CapRate,
    decimal VacanciaMedia,
    string Endereco);
}
