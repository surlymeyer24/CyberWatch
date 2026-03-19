namespace CyberWatch.Service.Models;

public class ResultadoCuarentena
{
    public bool Exitosa { get; set; }
    public string? RutaOriginal { get; set; }
    public string? RutaCuarentena { get; set; }
    public string? Error { get; set; }
}
