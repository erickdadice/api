using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using FastApiProcessor.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace FastApiProcessor.Utils
{
    public static class FileHelper
    {
        private static readonly string MetadataPath = Path.Combine("Data", "Metadata");

        public static async Task SaveMetadataAsync(UploadMetadataRequest metadata)
        {
            if (string.IsNullOrWhiteSpace(metadata.Id))
                throw new ArgumentException("El ID de metadata no puede estar vacío.", nameof(metadata.Id));

            if (!Directory.Exists(MetadataPath))
            {
                Directory.CreateDirectory(MetadataPath);
            }

            var filePath = Path.Combine(MetadataPath, $"{metadata.Id}.json");
            var json = JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true });

            try
            {
                // Escritura sincrónica forzada, garantizando flush
                using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
                using var sw = new StreamWriter(fs, Encoding.UTF8);
                await sw.WriteAsync(json);
                await sw.FlushAsync();
                fs.Flush(true); // Fuerza la escritura al disco físico
            }
            catch (IOException ex)
            {
                throw new IOException($"No se pudo guardar el archivo de metadata en {filePath}", ex);
            }
        }


        public static async Task<UploadMetadataRequest?> ReadMetadataAsync(string id)
        {
            var filePath = Path.Combine(MetadataPath, $"{id}.json");
            if (!File.Exists(filePath))
                return null;

            var json = await File.ReadAllTextAsync(filePath);
            return JsonSerializer.Deserialize<UploadMetadataRequest>(json);
        }

        public static async Task CreateZipFromBlobAsync(BlobServiceClient serviceClient, string containerName, string guid, Stream outputStream)
        {
            var containerClient = serviceClient.GetBlobContainerClient(containerName);

            if (!await containerClient.ExistsAsync())
                throw new InvalidOperationException($"El contenedor '{containerName}' no existe.");

            using var archive = new ZipArchive(outputStream, ZipArchiveMode.Create, true);

            await foreach (var blobItem in containerClient.GetBlobsAsync(prefix: $"{guid}/"))
            {
                var blobClient = containerClient.GetBlobClient(blobItem.Name);
                var entry = archive.CreateEntry(blobItem.Name.Replace($"{guid}/", ""));

                await using var entryStream = entry.Open();
                await using var blobStream = await blobClient.OpenReadAsync();
                await blobStream.CopyToAsync(entryStream);
            }
        }
        /*
                public static async Task<string> ReadMetadataParamStringAsync(string id)
                {
                    var metadata = await ReadMetadataAsync(id);
                    if (metadata == null) return string.Empty;

                    return $"{metadata.CodigoExamen} \"{metadata.TituloExamen}\" {metadata.VersionActual} {metadata.SinAnuladas} {metadata.Opcion} {metadata.Modelo}";
                }
        */

        public static async Task<string> ReadMetadataParamStringAsync(string id)
        {
            string filePath = Path.Combine("Data", "Metadata", $"{id}.json");


            if (!File.Exists(filePath))
            {
                Console.WriteLine($"[FileHelper] No se encontró el archivo de metadata: {filePath}");
                return string.Empty;
            }

            try
            {
                string json = await File.ReadAllTextAsync(filePath);
                Console.WriteLine($"[FileHelper] Contenido del JSON leído: {json}");

                var metadata = JsonSerializer.Deserialize<UploadMetadataRequest>(json);
                var result = $"{metadata.CodigoExamen} '{metadata.TituloExamen}' {metadata.VersionActual} {metadata.SinAnuladas} {metadata.Opcion} {metadata.Modelo}";
                Console.WriteLine($"[FileHelper] Resultado final de RSCRIPTPARAMS: {result}");
                return result;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FileHelper] Error leyendo o parseando metadata: {ex.Message}");
                return string.Empty;
            }
        }

    }
}
