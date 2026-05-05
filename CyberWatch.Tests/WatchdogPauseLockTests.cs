using CyberWatch.Shared.Helpers;
using Xunit;

namespace CyberWatch.Tests;

public class WatchdogPauseLockTests
{
    [Fact]
    public void Crear_Activo_Eliminar()
    {
        WatchdogPauseLock.Eliminar();
        Assert.False(WatchdogPauseLock.Activo());

        WatchdogPauseLock.Crear("test");
        Assert.True(WatchdogPauseLock.Activo());

        WatchdogPauseLock.Eliminar();
        Assert.False(WatchdogPauseLock.Activo());
    }

    [Fact]
    public void Activo_Falso_Si_Archivo_Muy_Viejo()
    {
        WatchdogPauseLock.Eliminar();
        WatchdogPauseLock.Crear("ttl");
        File.SetLastWriteTimeUtc(WatchdogPauseLock.LockPath, DateTime.UtcNow.AddHours(-2));

        Assert.False(WatchdogPauseLock.Activo(TimeSpan.FromMinutes(15)));

        WatchdogPauseLock.Eliminar();
    }
}
