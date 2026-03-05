/*
 * Script para volcar el estado de Firestore a archivos en el proyecto.
 * Así puedes compartir el contenido de la base de datos (o pegar aquí) para que
 * el asistente pueda ver la estructura y los datos.
 *
 * Requisitos:
 *   - Tener auth/serviceAccountKey.json en la raíz del repositorio (o configurar Firebase:CredentialPath en appsettings.json).
 *   - Ejecutar desde la raíz: dotnet run --project CyberWatch.DumpFirestore
 *
 * Genera:
 *   - firebase_dump.json  (volcado completo)
 *   - firebase_dump.md    (resumen legible)
 */

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Google.Cloud.Firestore;
using Microsoft.Extensions.Configuration;

string raiz = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
var config = new ConfigurationBuilder()
    .SetBasePath(raiz)
    .AddJsonFile("appsettings.json", optional: true)
    .AddJsonFile("appsettings.Development.json", optional: true)
    .Build();

var projectId = config["Firebase:ProjectId"] ?? "";
var credentialPath = config["Firebase:CredentialPath"] ?? "";
var colecciones = config.GetSection("Firebase:Colecciones").Get<string[]>() ?? new[] { "alertas", "config", "services", "cyberwatch_instancias" };
var dirSalida = config["Salida:Directorio"]?.Trim();
var nombreJson = config["Salida:NombreJson"] ?? "firebase_dump.json";
var nombreMd = config["Salida:NombreMd"] ?? "firebase_dump.md";

if (string.IsNullOrWhiteSpace(projectId))
{
    Console.WriteLine("Error: Firebase:ProjectId vacío en appsettings.json");
    Environment.Exit(1);
}

// Resolver ruta del JSON: probar varias ubicaciones
string? ResolveCredentialPath(string path)
{
    if (string.IsNullOrWhiteSpace(path)) return null;
    if (Path.IsPathRooted(path)) return File.Exists(path) ? path : null;
    var currentDir = Directory.GetCurrentDirectory();
    var baseDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..")); // raíz del repo (desde bin/Debug/net8.0)
    foreach (var basePath in new[] { currentDir, baseDir })
    {
        if (string.IsNullOrEmpty(basePath)) continue;
        var full = Path.Combine(basePath, path);
        if (File.Exists(full)) return full;
    }
    return null;
}

var pathToTry = credentialPath;

// Si no está configurado aquí, intentar usar el del servicio (misma raíz de repo)
if (string.IsNullOrWhiteSpace(pathToTry))
{
    var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
    var serviceAppsettings = Path.Combine(repoRoot, "CyberWatch.Service", "appsettings.json");
    if (File.Exists(serviceAppsettings))
    {
        var serviceConfig = new ConfigurationBuilder()
            .SetBasePath(Path.GetDirectoryName(serviceAppsettings)!)
            .AddJsonFile(Path.GetFileName(serviceAppsettings), optional: false)
            .Build();
        pathToTry = serviceConfig["Firebase:CredentialPath"]?.Trim() ?? "";
    }
}
if (string.IsNullOrWhiteSpace(pathToTry)) pathToTry = "auth/serviceAccountKey.json";

var resolvedCredential = ResolveCredentialPath(pathToTry);
if (resolvedCredential == null && Path.IsPathRooted(pathToTry) && File.Exists(pathToTry))
    resolvedCredential = pathToTry;
if (resolvedCredential == null && !Path.IsPathRooted(pathToTry))
    resolvedCredential = File.Exists(pathToTry) ? Path.GetFullPath(pathToTry) : null;

if (string.IsNullOrWhiteSpace(resolvedCredential) || !File.Exists(resolvedCredential))
{
    Console.WriteLine("Error: No se encontró el JSON de la cuenta de servicio de Firebase.");
    Console.WriteLine("  - Crea la carpeta 'auth' en la raíz del repo y coloca ahí 'serviceAccountKey.json' (descargado desde Firebase Console).");
    Console.WriteLine("  - O configura 'Firebase:CredentialPath' en CyberWatch.Service/appsettings.json o en CyberWatch.DumpFirestore/appsettings.json con la ruta completa al archivo.");
    Environment.Exit(1);
}
credentialPath = resolvedCredential;

Environment.SetEnvironmentVariable("GOOGLE_APPLICATION_CREDENTIALS", Path.GetFullPath(credentialPath));
var baseDir = string.IsNullOrWhiteSpace(dirSalida) ? Directory.GetCurrentDirectory() : Path.GetFullPath(dirSalida);

FirestoreDb? db = null;
try
{
    db = FirestoreDb.Create(projectId);
}
catch (Exception ex)
{
    Console.WriteLine("Error conectando a Firestore: " + ex.Message);
    Environment.Exit(1);
}

if (db == null)
{
    Console.WriteLine("Error: no se pudo crear el cliente Firestore.");
    Environment.Exit(1);
}

Console.WriteLine("Leyendo Firestore (proyecto: {0})...", projectId);

var estado = new Dictionary<string, object>
{
    ["_exportado_en"] = DateTime.UtcNow.ToString("o"),
    ["_proyecto"] = projectId
};

foreach (var colId in colecciones)
{
    try
    {
        var snap = await db.Collection(colId).GetSnapshotAsync();
        var docs = new Dictionary<string, object>();
        foreach (var doc in snap.Documents)
        {
            var serialized = ToSerializable(doc.ToDictionary());
            if (serialized != null)
                docs[doc.Id] = serialized;
        }
        estado[colId] = docs;
    }
    catch (Exception ex)
    {
        estado[colId] = new Dictionary<string, object> { ["_error"] = ex.Message };
    }
}

// JSON
var jsonPath = Path.Combine(baseDir, nombreJson);
await using (var f = File.Create(jsonPath))
{
    await JsonSerializer.SerializeAsync(f, estado, new JsonSerializerOptions { WriteIndented = true });
}
Console.WriteLine("Escrito: " + jsonPath);

// Markdown
var mdPath = Path.Combine(baseDir, nombreMd);
await using (var f = new StreamWriter(mdPath, false, System.Text.Encoding.UTF8))
{
    await f.WriteLineAsync("# Volcado Firestore - CyberWatch");
    await f.WriteLineAsync();
    await f.WriteLineAsync("Exportado: " + estado["_exportado_en"]);
    await f.WriteLineAsync();
    foreach (var col in colecciones)
    {
        await f.WriteLineAsync("## Colección: " + col);
        await f.WriteLineAsync();
        if (estado[col] is not Dictionary<string, object> data)
            continue;
        if (data.TryGetValue("_error", out var err))
        {
            await f.WriteLineAsync("Error: " + err);
            await f.WriteLineAsync();
            continue;
        }
        foreach (var kv in data)
        {
            if (kv.Key.StartsWith("_")) continue;
            await f.WriteLineAsync("### Documento: `" + kv.Key + "`");
            await f.WriteLineAsync();
            if (kv.Value is Dictionary<string, object> docData)
            {
                await f.WriteLineAsync("```json");
                await f.WriteLineAsync(JsonSerializer.Serialize(docData, new JsonSerializerOptions { WriteIndented = true }));
                await f.WriteLineAsync("```");
            }
            else
                await f.WriteLineAsync("(sin datos)");
            await f.WriteLineAsync();
        }
    }
}
Console.WriteLine("Escrito: " + mdPath);
Console.WriteLine();
Console.WriteLine("Puedes abrir firebase_dump.md o firebase_dump.json y compartir su contenido aquí para que revise la base de datos.");

// Convierte valores de Firestore a tipos serializables para JSON
static object? ToSerializable(object? v)
{
    if (v == null) return null;
    if (v is Dictionary<string, object> d)
    {
        var outDict = new Dictionary<string, object>();
        foreach (var kv in d)
        {
            var val = ToSerializable(kv.Value);
            if (val != null) outDict[kv.Key] = val;
        }
        return outDict;
    }
    if (v is IEnumerable<object> list)
    {
        var outList = new List<object?>();
        foreach (var item in list)
        {
            var val = ToSerializable(item);
            if (val != null) outList.Add(val);
        }
        return outList;
    }
    if (v is Google.Cloud.Firestore.Timestamp ts)
        return ts.ToDateTime().ToUniversalTime().ToString("o");
    if (v is DateTime dt)
        return dt.ToUniversalTime().ToString("o");
    if (v is bool or int or long or double or string)
        return v;
    return v.ToString();
}
