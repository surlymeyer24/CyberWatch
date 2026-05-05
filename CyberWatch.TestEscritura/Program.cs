// Simulador de escritura masiva + renombrados masivos SIN extensiones conocidas.
// Prueba la detección por COMPORTAMIENTO puro (no por extensión sospechosa).
//
// Scoring esperado:
//   - escriturasSospechosas = true (500+ escrituras en 10s)  → +1
//   - renombradosSospechosas = true (100+ renombrados en 10s) → +1
//   - Combinados → +3  (patrón clásico ransomware)
//   - extensionSospechosa = false (usa .xyz, no .encrypted)  → +0
//   - TOTAL = 3 → debe disparar alerta
//
// USO:
//   dotnet publish CyberWatch.TestMalware -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:StartupObject=TestEscrituraMasiva -o ./test_escritura
//   .\test_escritura\CyberWatch.TestMalware.exe

var carpeta = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
    "CyberWatchTestEscritura");

Directory.CreateDirectory(carpeta);

Console.WriteLine($"[TestEscritura] PID: {Environment.ProcessId}");
Console.WriteLine($"[TestEscritura] EXE: {Environment.ProcessPath}");
Console.WriteLine($"[TestEscritura] Carpeta: {carpeta}");
Console.WriteLine();
Console.WriteLine("[TestEscritura] Este test NO usa extensiones sospechosas.");
Console.WriteLine("[TestEscritura] Detecta por comportamiento: escrituras masivas + renombrados masivos.");
Console.WriteLine();

// Fase 1: crear 600 archivos rápido (superar umbral de 500 escrituras en 10s)
Console.WriteLine("[TestEscritura] Fase 1: Creando 600 archivos .txt...");
for (int i = 0; i < 600; i++)
{
    var archivo = Path.Combine(carpeta, $"data_{i}.txt");
    File.WriteAllText(archivo, $"Contenido original #{i} - {DateTime.Now:O}");
    if (i % 100 == 0) Console.WriteLine($"  Creados {i}/600...");
    Thread.Sleep(5); // ~200 archivos/segundo
}

// Fase 2: renombrar 150 archivos (superar umbral de 100 renombrados en 10s)
// Usa extensión .xyz (no está en la lista de sospechosas)
Console.WriteLine("[TestEscritura] Fase 2: Renombrando 150 archivos a .xyz...");
for (int i = 0; i < 150; i++)
{
    var original = Path.Combine(carpeta, $"data_{i}.txt");
    var nuevo = Path.Combine(carpeta, $"data_{i}.xyz");
    if (File.Exists(original))
    {
        File.Move(original, nuevo);
        File.WriteAllText(nuevo, "CONTENIDO MODIFICADO");
    }
    if (i % 50 == 0) Console.WriteLine($"  Renombrados {i}/150...");
    Thread.Sleep(5);
}

Console.WriteLine();
Console.WriteLine("[TestEscritura] Si llegaste hasta acá, el Service NO te mató.");
Console.WriteLine("[TestEscritura] Revisá el log para entender por qué.");
Console.WriteLine();
Console.WriteLine("Presioná Enter para limpiar y salir...");
Console.ReadLine();

if (Directory.Exists(carpeta))
{
    Directory.Delete(carpeta, true);
    Console.WriteLine("[TestEscritura] Carpeta de prueba eliminada.");
}
