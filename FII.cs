namespace FIIsCLI;

/// <summary>
/// Uma linha da tabela de fundos imobiliários do Fundamentus.
/// Os campos percentuais (FfoYeld, DividendYield, CapRate, VacanciaMedia)
/// são armazenados como fração — 8,5% vira 0.085m.
/// </summary>
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
