namespace CyberWatch.Shared.Helpers;

/// <summary>
/// Lectura del identificador de máquina guardado por RegistroInstanciaFirebaseService.
/// No genera el ID: esa responsabilidad es del servicio que lo crea.
/// </summary>
public static class MachineIdHelper
{
    public static string? Read()
    {
        try
        {
            var idFile = Path.Combine(AppContext.BaseDirectory, "cyberwatch_machine_id.txt");
            if (File.Exists(idFile))
            {
                var id = File.ReadAllText(idFile).Trim();
                if (!string.IsNullOrEmpty(id) && id.Length >= 8)
                    return id;
            }
        }
        catch { /* ignore */ }
        return null;
    }
}
