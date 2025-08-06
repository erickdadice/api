using Azure;
using Azure.Core;
using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Sas;
using FastApiProcessor.Models;
using FastApiProcessor.Utils;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Text;
using System.Net.Http.Headers;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace FastApiProcessor.Services
{
    public class ProcessFiles : IProcessFiles
    {
        private readonly BlobServiceClient _blobServiceClient;
        private readonly ILogger<ProcessFiles> _logger;
        private readonly string _containerName;
        private readonly TokenCredential _credential;
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;
        private readonly bool _useUserDelegationKey;
        private readonly string _containerImage;




        // Variables de entorno para Azure Container App Job
        private readonly BlobContainerClient _blobContainerClient;
        private readonly string _subscriptionId = Environment.GetEnvironmentVariable("AZURE_SUBSCRIPTION_ID")!;
        private readonly string _resourceGroupName = Environment.GetEnvironmentVariable("AZURE_RESOURCE_GROUP")!;
        private readonly string _containerAppJobName = Environment.GetEnvironmentVariable("AZURE_CONTAINER_APP_JOB_NAME")!;
        private readonly string? _storageAccountName;
        private readonly string? _managedIdentityClientId;

        public ProcessFiles(IConfiguration configuration, ILogger<ProcessFiles> logger)
        {
            _configuration = configuration;
            _logger = logger;

            _storageAccountName = configuration["BLOB_ACCOUNT_NAME"] ?? "mirazrjobststsa";
            _containerName = configuration["BLOB_CONTAINER_NAME"] ?? "rjobs-results";
            _managedIdentityClientId = configuration["AzureManagedIdentity:ClientId"];
            var connectionString = configuration["AzureBlobStorage:ConnectionString"];

            // Inicializar DefaultAzureCredential por si se necesita más adelante
            _credential = string.IsNullOrEmpty(_managedIdentityClientId)
                ? new DefaultAzureCredential()
                : new DefaultAzureCredential(new DefaultAzureCredentialOptions
                {
                    ManagedIdentityClientId = _managedIdentityClientId
                });

            if (!string.IsNullOrWhiteSpace(connectionString))
            {
                _logger.LogWarning("[INIT] Inicializando BlobServiceClient con Connection String.");
                _blobServiceClient = new BlobServiceClient(connectionString);
                _useUserDelegationKey = false;
            }
            else
            {
                _logger.LogInformation("[INIT] Inicializando BlobServiceClient con identidad administrada.");
                _blobServiceClient = new BlobServiceClient(
                    new Uri($"https://{_storageAccountName}.blob.core.windows.net"),
                    _credential
                );
                _useUserDelegationKey = true; // 
            }

            _httpClient = new HttpClient();
            _blobContainerClient = _blobServiceClient.GetBlobContainerClient(_containerName);

            _logger.LogInformation("[INIT] Inicialización de ProcessFiles completada correctamente.");
        }




        public async Task<NotifyResult> NotifyOutSystemsProcessFinishedAsync(
            string id,
            string entorno,
            string? errorOnAzure = null)
        {
            try
            {
                _logger.LogInformation(
                    "[OUTSYSTEMS] Iniciando notificación a ProccessFinished para ID: {id} en entorno: {entorno}",
                    id, entorno);

                //  Construir URL base dinámicamente
                string baseUrl = entorno.ToUpper() == "PRO"
                    ? "https://uax.outsystems.app"
                    : $"https://uax-{entorno.ToLower()}.outsystems.app";

                string tokenUrl = $"{baseUrl}/HomeCentral/rest/OAuth2/Token";

                //  Determinar estado final
                var isFailed = !string.IsNullOrWhiteSpace(errorOnAzure) &&
                               (errorOnAzure.Equals("Failed", StringComparison.OrdinalIgnoreCase) ||
                                errorOnAzure.Equals("Stopped", StringComparison.OrdinalIgnoreCase));

                string estadoFinal = isFailed ? "Failed" : "Completed";
                string mensaje = isFailed
                    ? (string.IsNullOrWhiteSpace(errorOnAzure) ? "Job detenido o fallido" : errorOnAzure)
                    : "Ejecución exitosa";

                //  Construir URL de POST
                string postUrl = $"{baseUrl}/Course/rest/RGraphics/ProccessFinished" +
                                 $"?ProccessId={id}" +
                                 $"&estadoFinal={Uri.EscapeDataString(estadoFinal)}" +
                                 $"&mensaje={Uri.EscapeDataString(mensaje)}";

                //  Variables de entorno para credenciales
                string clientId = Environment.GetEnvironmentVariable($"OUTSYSTEMS_CLIENT_ID_{entorno.ToUpper()}")
                                  ?? "1694791d3cdf469899f2"; // fallback DEV

                string clientSecret = Environment.GetEnvironmentVariable($"OUTSYSTEMS_CLIENT_SECRET_{entorno.ToUpper()}")
                                      ?? "3700d77b79864e35879e46bc252ce8b5cb4d9e644ca44f2497165d82da18"; // fallback DEV

                //  Obtener token
                var tokenBody = new StringContent(
                    $"client_id={clientId}&client_secret={clientSecret}",
                    Encoding.UTF8,
                    "application/x-www-form-urlencoded");

                var tokenResponse = await _httpClient.PostAsync(tokenUrl, tokenBody);
                var tokenJson = await tokenResponse.Content.ReadAsStringAsync();

                if (!tokenResponse.IsSuccessStatusCode)
                {
                    _logger.LogError("[OUTSYSTEMS] Error al obtener token: {status} {body}", tokenResponse.StatusCode, tokenJson);
                    return new NotifyResult(false, $"Fallo al obtener token. Status: {tokenResponse.StatusCode}");
                }

                var tokenNode = JsonNode.Parse(tokenJson);
                var accessToken = tokenNode?["access_token"]?.ToString();

                if (string.IsNullOrWhiteSpace(accessToken))
                {
                    _logger.LogError("[OUTSYSTEMS] access_token no presente en respuesta.");
                    return new NotifyResult(false, "Token inválido o no presente en la respuesta.");
                }

                //  POST solo con Bearer, sin body
                var postRequest = new HttpRequestMessage(HttpMethod.Post, postUrl)
                {
                    Headers = { Authorization = new AuthenticationHeaderValue("Bearer", accessToken) }
                };

                var postResponse = await _httpClient.SendAsync(postRequest);
                var responseBody = await postResponse.Content.ReadAsStringAsync();

                if (postResponse.IsSuccessStatusCode)
                {
                    _logger.LogInformation("[OUTSYSTEMS] Notificación enviada correctamente. Body respuesta: {response}", responseBody);
                    return new NotifyResult(true, $"Respuesta de OutSystems: {responseBody}");
                }
                else
                {
                    _logger.LogWarning("[OUTSYSTEMS] Código no exitoso: {code}. Body: {body}", postResponse.StatusCode, responseBody);
                    return new NotifyResult(false, $"Código HTTP: {postResponse.StatusCode}, Body: {responseBody}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[OUTSYSTEMS] Error inesperado notificando a OutSystems.");
                return new NotifyResult(false, "Excepción en la llamada a OutSystems.");
            }
        }

        public async Task<List<string>> ProcessAndUploadFilesAsync(IFormFile[] files, string guid)
        {
            var urls = new List<string>();
            var containerClient = _blobServiceClient.GetBlobContainerClient(_containerName);
            await containerClient.CreateIfNotExistsAsync();

            foreach (var file in files)
            {
                var blobClient = containerClient.GetBlobClient($"{guid}/{file.FileName}");

                
                await using var stream = file.OpenReadStream();
                await blobClient.UploadAsync(stream, overwrite: true);

                urls.Add(blobClient.Uri.ToString());
            }

            return urls;
        }



        public async Task<string> UploadFilesWithMetadataAsync(IFormFile file1, IFormFile file2, IFormFile file3,
            string cod, string titulo, int ver, int sinAnul, int opcion, int modelo, string entorno)
        {
            var id = IdGenerator.GenerateUniqueId();
            await ProcessAndUploadFilesAsync(new[] { file1, file2, file3 }, id);

            var metadata = new UploadMetadataRequest
            {
                Id = id,
                CodigoExamen = cod,
                TituloExamen = titulo,
                VersionActual = ver,
                SinAnuladas = sinAnul,
                Opcion = opcion,
                Modelo = modelo,
                Entorno = entorno,
                Fichero1Nombre = file1.FileName,
                Fichero2Nombre = file2.FileName,
                Fichero3Nombre = file3.FileName
            };

            await FileHelper.SaveMetadataAsync(metadata);
            return id;
        }

        public Task<string> UploadAndStoreMetadataAsync(IFormFile f1, IFormFile f2, IFormFile f3,
            string cod, string titulo, int ver, int sinAnul, int opcion, int modelo, string entorno)
        {
            return UploadFilesWithMetadataAsync(f1, f2, f3, cod, titulo, ver, sinAnul, opcion, modelo, entorno);
        }

        public async Task<string> UploadFilesBase64WithMetadataAsync(UploadMetadataBase64Request request)
        {
            var id = IdGenerator.GenerateUniqueId();

            _logger.LogInformation("[BASE64] Iniciando procesamiento de archivos base64. ID generado: {id}", id);

            try
            {
                // 1. Convertir base64 a byte[] y subir a Blob Storage
                await UploadBase64FileAsync(request.Fichero1Contenido, id, request.Fichero1Nombre);
                await UploadBase64FileAsync(request.Fichero2Contenido, id, request.Fichero2Nombre);
                await UploadBase64FileAsync(request.Fichero3Contenido, id, request.Fichero3Nombre);

                _logger.LogInformation("[BASE64] Archivos subidos correctamente al contenedor con ID: {id}", id);

                // 2. Crear metadata equivalente a UploadMetadataRequest y guardarla
                var metadata = new UploadMetadataRequest
                {
                    Id = id,
                    CodigoExamen = request.CodigoExamen,
                    TituloExamen = request.TituloExamen,
                    VersionActual = request.VersionActual,
                    SinAnuladas = request.SinAnuladas,
                    Opcion = request.Opcion,
                    Modelo = request.Modelo,
                    Entorno = request.Entorno,
                    Fichero1Nombre = request.Fichero1Nombre,
                    Fichero2Nombre = request.Fichero2Nombre,
                    Fichero3Nombre = request.Fichero3Nombre
                };

                await FileHelper.SaveMetadataAsync(metadata);

                _logger.LogInformation("[BASE64] Metadata guardada correctamente para ID: {id}", id);

                return id;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[BASE64] Error durante UploadFilesBase64WithMetadataAsync para ID: {id}", id);
                throw;
            }
        }


        private async Task UploadBase64FileAsync(string base64Content, string id, string fileName)
        {
            byte[] fileBytes = Convert.FromBase64String(base64Content);

            var blobClient = _blobServiceClient
                .GetBlobContainerClient(_containerName)
                .GetBlobClient($"{id}/{fileName}");

            await using var stream = new MemoryStream(fileBytes);
            await blobClient.UploadAsync(stream, overwrite: true);

            _logger.LogInformation("[BASE64] Archivo subido: {file}", fileName);
        }


        public async Task<List<string>> GenerateUserDelegationSasUrlsAsync(string guid)
        {
            var result = new List<string>();
            var containerClient = _blobServiceClient.GetBlobContainerClient(_containerName);

            if (!_useUserDelegationKey)
            {
                _logger.LogWarning("[SAS] No se puede generar SAS con User Delegation Key en modo conexión local.");
                return result;
            }


            var keyResponse = await _blobServiceClient.GetUserDelegationKeyAsync(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1));
            var key = keyResponse.Value;

            await foreach (var blob in containerClient.GetBlobsAsync(prefix: $"{guid}/"))
            {
                var blobClient = containerClient.GetBlobClient(blob.Name);
                var sasBuilder = new BlobSasBuilder
                {
                    BlobContainerName = _containerName,
                    BlobName = blob.Name,
                    Resource = "b",
                    ExpiresOn = DateTimeOffset.UtcNow.AddMinutes(10),


                };
                sasBuilder.SetPermissions(BlobSasPermissions.Read);

                var sasToken = sasBuilder.ToSasQueryParameters(key, _blobServiceClient.AccountName);
                var uriBuilder = new UriBuilder(blobClient.Uri)
                {
                    Query = sasToken.ToString()
                };
                result.Add(uriBuilder.Uri.ToString());
            }

            return result;
        }

        public async Task<string> GenerateUserDelegationSasUrlForFileAsync(string guid, string fileName)
        {
            var containerClient = _blobServiceClient.GetBlobContainerClient(_containerName);

            if (!_useUserDelegationKey)
            {
                _logger.LogWarning("[SAS] No se puede generar SAS con User Delegation Key en modo conexión local.");
                return string.Empty;
            }


            var keyResponse = await _blobServiceClient.GetUserDelegationKeyAsync(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1));
            var key = keyResponse.Value;

            var blobClient = containerClient.GetBlobClient($"{guid}/{fileName}");

            var sasBuilder = new BlobSasBuilder
            {
                BlobContainerName = _containerName,
                BlobName = $"{guid}/{fileName}",
                Resource = "b",
                ExpiresOn = DateTimeOffset.UtcNow.AddMinutes(10),

            };
            sasBuilder.SetPermissions(BlobSasPermissions.Read);

            var sasToken = sasBuilder.ToSasQueryParameters(key, _blobServiceClient.AccountName);
            var uriBuilder = new UriBuilder(blobClient.Uri)
            {
                Query = sasToken.ToString()
            };
            return uriBuilder.Uri.ToString();
        }

        public BlobClient GetBlobClient(string guid, string fileName)
        {
            var containerClient = _blobServiceClient.GetBlobContainerClient(_containerName);
            return containerClient.GetBlobClient($"{guid}/{fileName}");
        }

        public async Task<BinaryFileResult?> GetFileBinaryAsync(string guid, string fileName)
        {
            var blobClient = GetBlobClient(guid, $"output/{fileName}");
            if (!await blobClient.ExistsAsync()) return null;

            var response = await blobClient.DownloadAsync();
            using var ms = new MemoryStream();
            await response.Value.Content.CopyToAsync(ms);

            return new BinaryFileResult(ms.ToArray(), response.Value.Details.ContentType, fileName);
        }

        public async Task<MemoryStream> DownloadAllFilesAsZipAsync(string guid)
        {
            var ms = new MemoryStream();
            await FileHelper.CreateZipFromBlobAsync(_blobServiceClient, _containerName, guid, ms);
            ms.Seek(0, SeekOrigin.Begin);
            return ms;
        }

        public async Task<bool> DeleteFileAsync(string guid, string fileName)
        {
            var blobClient = GetBlobClient(guid, $"output/{fileName}");
            return await blobClient.DeleteIfExistsAsync();
        }

        public async Task<List<string>> DeleteAllFilesByIdAsync(string guid)
        {
            var deleted = new List<string>();
            var containerClient = _blobServiceClient.GetBlobContainerClient(_containerName);

            await foreach (var blob in containerClient.GetBlobsAsync(prefix: $"{guid}/"))
            {
                var blobClient = containerClient.GetBlobClient(blob.Name);
                await blobClient.DeleteIfExistsAsync();
                deleted.Add(blob.Name ?? string.Empty);
            }

            return deleted;
        }

        public List<string> ListAllIds()
        {
            var folderPath = Path.Combine("Data", "Metadata");
            return Directory.Exists(folderPath)
                ? Directory.GetFiles(folderPath, "*.json").Select(f => Path.GetFileNameWithoutExtension(f) ?? string.Empty).ToList()
                : new List<string>();
        }

        public List<string> ListFileNamesById(string guid)
        {
            var containerClient = _blobServiceClient.GetBlobContainerClient(_containerName);
            return containerClient.GetBlobs(prefix: $"{guid}/").Select(b => b.Name ?? string.Empty).ToList();
        }

        public List<string> ListIdsByDate(string yyyyMMdd)
        {
            var folderPath = Path.Combine("Data", "Metadata");
            return Directory.Exists(folderPath)
                ? Directory.GetFiles(folderPath, "*.json")
                    .Where(f => File.GetCreationTimeUtc(f).ToString("yyyyMMdd") == yyyyMMdd)
                    .Select(f => Path.GetFileNameWithoutExtension(f) ?? string.Empty)
                    .ToList()
                : new List<string>();
        }

        public async Task<string> FullProcessAsync(IFormFile f1, IFormFile f2, IFormFile f3, string cod, string titulo, int ver, int sinAnul, int opcion, int modelo, string entorno)
        {
            var id = await UploadAndStoreMetadataAsync(f1, f2, f3, cod, titulo, ver, sinAnul, opcion, modelo, entorno);
            _ = TriggerAzureContainerJobAsync(id, entorno);
            return id;
        }

        public Task<bool> UpdateStatusAsync(string id, string status)
        {
            _logger.LogInformation($"Actualizando estado del proceso {id} a {status}");
            return Task.FromResult(true);
        }

        public Task ConfirmProcessExternallyAsync(string id)
        {
            _logger.LogInformation($"Simulación de confirmación externa del proceso {id}");
            return Task.CompletedTask;
        }

        public async Task<bool> LaunchJobOnlyAsync(string id)
        {
            var jobId = await TriggerAzureContainerJobAsync(id, "DEV");
            return !string.IsNullOrEmpty(jobId);
        }


        public async Task<bool> LaunchJobViaCliFallbackAsync(string jobName, string resourceGroup, string directoryId, string rscriptParams)
        {
            try
            {
                string cliCommand = $"containerapp job start --name {jobName} --resource-group {resourceGroup} " +
                                    $"--env-vars DIRECTORY={directoryId} RSCRIPTPARAMS=\"{rscriptParams}\"";

                var processInfo = new ProcessStartInfo("az")
                {
                    Arguments = cliCommand,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                _logger.LogInformation($"Ejecutando CLI: az {cliCommand}");

                using var process = new Process { StartInfo = processInfo };
                process.Start();

                string output = await process.StandardOutput.ReadToEndAsync();
                string error = await process.StandardError.ReadToEndAsync();

                await process.WaitForExitAsync();

                _logger.LogInformation("Salida CLI: " + output);
                if (!string.IsNullOrWhiteSpace(error))
                    _logger.LogError("Error CLI: " + error);

                return process.ExitCode == 0;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error al ejecutar CLI fallback: {ex.Message}");
                return false;
            }
        }

        public async Task<string> ReadMetadataParamStringAsync(string id)
        {
            try
            {
                _logger.LogInformation("[PARAMS] Leyendo metadata para ID: {id}", id);
                return await FileHelper.ReadMetadataParamStringAsync(id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[PARAMS] Error al leer los parámetros del metadata para ID: {id}", id);
                throw;
            }
        }

        public async Task<string> GetJobStatusAsync(string id)
        {
            try
            {
                var token = await _credential.GetTokenAsync(
                    new TokenRequestContext(new[] { "https://management.azure.com/.default" }),
                    CancellationToken.None);

                var url = $"https://management.azure.com/subscriptions/{_subscriptionId}/resourceGroups/{_resourceGroupName}/providers/Microsoft.App/jobs/{_containerAppJobName}/executions?api-version=2023-11-02-preview";

                var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);

                var response = await _httpClient.SendAsync(request);
                var body = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError("[STATUS] Falló consulta de ejecuciones. Código: {code}. Body: {body}", response.StatusCode, body);
                    return "Error";
                }

                using var json = JsonDocument.Parse(body);
                if (!json.RootElement.TryGetProperty("value", out var executions)) return "Unknown";

                JsonElement? match = null;
                DateTimeOffset? latestStart = null;

                foreach (var exec in executions.EnumerateArray())
                {
                    if (exec.TryGetProperty("properties", out var props)
                        && props.TryGetProperty("template", out var template)
                        && template.TryGetProperty("containers", out var containers))
                    {
                        foreach (var container in containers.EnumerateArray())
                        {
                            if (container.TryGetProperty("env", out var envVars))
                            {
                                foreach (var env in envVars.EnumerateArray())
                                {
                                    if (env.GetProperty("name").GetString() == "DIRECTORY"
                                        && env.GetProperty("value").GetString() == id)
                                    {
                                        // Verifica cuál es el más reciente
                                        if (props.TryGetProperty("startTime", out var start)
                                            && DateTimeOffset.TryParse(start.GetString(), out var parsed))
                                        {
                                            if (latestStart == null || parsed > latestStart)
                                            {
                                                match = exec;
                                                latestStart = parsed;
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }

                if (match != null)
                {
                    var status = match.Value.GetProperty("properties").GetProperty("status").GetString();
                    _logger.LogInformation("[STATUS] Job run con ID {id} tiene estado: {status}", id, status);
                    return status ?? "Unknown";
                }

                _logger.LogWarning("[STATUS] No se encontró ejecución reciente con DIRECTORY = {id}", id);
                return "Running";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[STATUS] Error inesperado al consultar ejecuciones del job para ID: {id}", id);
                return "Error";
            }
        }

        public async Task<List<object>> GenerateUserDelegationSasUrlsForOutputAsync(string id)
        {
            var containerName = "rjobs-results";
            var prefix = $"{id}/output/";
            var results = new List<object>();

            var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);

            await foreach (var blobItem in containerClient.GetBlobsAsync(prefix: prefix))
            {
                var blobClient = containerClient.GetBlobClient(blobItem.Name);

                var userDelegationKey = await _blobServiceClient.GetUserDelegationKeyAsync(
                    DateTimeOffset.UtcNow,
                    DateTimeOffset.UtcNow.AddHours(2));

                var sasBuilder = new BlobSasBuilder
                {
                    BlobContainerName = containerName,
                    BlobName = blobItem.Name,
                    Resource = "b",
                    ExpiresOn = DateTimeOffset.UtcNow.AddMinutes(15),

                };

                sasBuilder.SetPermissions(BlobSasPermissions.Read);

                var sasToken = sasBuilder.ToSasQueryParameters(userDelegationKey, _storageAccountName);

                var uriBuilder = new UriBuilder(blobClient.Uri)
                {
                    Query = sasToken.ToString()
                };

                results.Add(new
                {
                    nombre = Path.GetFileName(blobItem.Name),
                    url = uriBuilder.ToString()
                });
            }

            return results;
        }

        private async Task<string> GetAccessTokenAsync()
        {
            var scope = "https://management.azure.com/.default"; // <- Esto debes definirlo

            var credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions
            {
                ManagedIdentityClientId = _configuration["AzureManagedIdentity:ClientId"]
            });

            var token = await credential.GetTokenAsync(
                new TokenRequestContext(new[] { scope }),
                CancellationToken.None
            );

            return token.Token;
        }

        public async Task<string> TriggerAzureContainerJobAsync(string id, string entorno)
        {
            _logger.LogInformation("[JOB] 🟢 Iniciando ejecución del job en background para ID: {id} y entorno: {entorno}", id, entorno);

            // 🚀 Devolver el ID inmediatamente y correr el proceso completo en segundo plano
            _ = Task.Run(async () =>
            {
                try
                {
                    // 1. Leer metadata
                    _logger.LogInformation("[JOB]  Leyendo metadata para ID: {id}", id);
                    string rscriptParams = await FileHelper.ReadMetadataParamStringAsync(id);

                    if (string.IsNullOrEmpty(rscriptParams))
                    {
                        _logger.LogWarning("[JOB]  No se encontraron parámetros RSCRIPTPARAMS válidos para ID: {id}", id);
                        return;
                    }

                    _logger.LogInformation("[JOB]  RSCRIPTPARAMS cargados: {params}", rscriptParams);

                    // 2. Obtener la imagen desde variable de entorno
                    var containerImage = Environment.GetEnvironmentVariable("JOB_CONTAINER_IMAGE")
                                        ?? "mirazrjobststacr.azurecr.io/rjob:1.0";
                    _logger.LogInformation("[JOB]  Usando imagen del contenedor: {image}", containerImage);

                    // 3. Obtener token de acceso
                    _logger.LogInformation("[JOB]  Solicitando token de acceso...");
                    var accessToken = await _credential.GetTokenAsync(
                        new TokenRequestContext(new[] { "https://management.azure.com/.default" }),
                        CancellationToken.None
                    );
                    _logger.LogInformation("[JOB]  Token obtenido correctamente");

                    // 4. PATCH: actualizar entorno del job
                    _logger.LogInformation("[JOB]  Enviando PATCH para actualizar variables de entorno...");
                    var patchUrl = $"https://management.azure.com/subscriptions/{_subscriptionId}/resourceGroups/{_resourceGroupName}/providers/Microsoft.App/jobs/{_containerAppJobName}?api-version=2023-11-02-preview";

                    var patchPayload = new
                    {
                        properties = new
                        {
                            template = new
                            {
                                containers = new[]
                                {
                            new
                            {
                                name = "main",
                                image = containerImage,
                                env = new[]
                                {
                                    new { name = "DIRECTORY", value = id },
                                    new { name = "RSCRIPTPARAMS", value = rscriptParams },
                                    new { name = "TRIGGER_SOURCE", value = "API_FULLPROCESS" },
                                    new { name = "MANAGED_IDENTITY_CLIENT_ID", value = "79089693-70bf-46c7-9445-2ec8624550d2" },
                                    new { name = "STORAGE_ACCOUNT", value = "mirazrjobststsa" },
                                    new { name = "CONTAINER_NAME", value = "rjobs-results" }
                                }
                            }
                        }
                            }
                        }
                    };

                    var patchRequest = new HttpRequestMessage(HttpMethod.Patch, patchUrl)
                    {
                        Headers = { Authorization = new AuthenticationHeaderValue("Bearer", accessToken.Token) },
                        Content = new StringContent(JsonSerializer.Serialize(patchPayload), Encoding.UTF8, "application/json")
                    };

                    var patchResponse = await _httpClient.SendAsync(patchRequest);
                    var patchBody = await patchResponse.Content.ReadAsStringAsync();

                    if (!patchResponse.IsSuccessStatusCode)
                    {
                        _logger.LogError("[JOB]  PATCH fallido. Código: {code}. Detalles: {body}", patchResponse.StatusCode, patchBody);
                        return;
                    }

                    _logger.LogInformation("[JOB]  PATCH exitoso. Esperando 1 segundo antes de lanzar el job...");
                    await Task.Delay(1000);

                    // 5. POST: lanzar el job
                    _logger.LogInformation("[JOB]  Enviando POST para lanzar el job...");
                    var postUrl = $"https://management.azure.com/subscriptions/{_subscriptionId}/resourceGroups/{_resourceGroupName}/providers/Microsoft.App/jobs/{_containerAppJobName}/start?api-version=2023-11-02-preview";

                    var postRequest = new HttpRequestMessage(HttpMethod.Post, postUrl)
                    {
                        Headers = { Authorization = new AuthenticationHeaderValue("Bearer", accessToken.Token) }
                    };

                    var postResponse = await _httpClient.SendAsync(postRequest);
                    var postBody = await postResponse.Content.ReadAsStringAsync();

                    if (!postResponse.IsSuccessStatusCode)
                    {
                        _logger.LogError("[JOB]  POST fallido. Código: {code}. Detalles: {body}", postResponse.StatusCode, postBody);
                        return;
                    }

                    _logger.LogInformation("[JOB]  Job lanzado correctamente para ID: {id}", id);

                    // 6. Monitorear ejecución
                    _logger.LogInformation("[MONITOREO]  Iniciando monitoreo para job con ID: {id}", id);
                    string finalStatus = await MonitorJobRunUntilCompleteAsync(id);
                    _logger.LogInformation("[MONITOREO]  Job ID {id} finalizó con estado: {status}", id, finalStatus);

                    // 7. Confirmación externa
                    _logger.LogInformation("[OUTSYSTEMS]  Notificando estado final: {status}", finalStatus);
                    await NotifyOutSystemsProcessFinishedAsync(id, entorno, finalStatus == "Failed" ? "ErrorOnAzure" : null);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[JOB]  Excepción inesperada al lanzar job en background para ID: {id}", id);
                }
            });

            //  Devuelve el ID
            return id;
        }

        public async Task<string> MonitorJobRunUntilCompleteAsync(string id)
        {
            string estado = "Running";
            int intentos = 0;
            int maxIntentos = 120;
            int delaySegundos = 10;

            _logger.LogInformation("[MONITOREO] Iniciando monitoreo para job con ID: {id}");

            while (estado == "Running" && intentos < maxIntentos)
            {
                estado = await GetJobStatusAsync(id);

                _logger.LogInformation("[MONITOREO] Intento {intentos}: estado = {estado}", intentos + 1, estado);

                if (estado == "Completed" || estado == "Failed" || estado == "Error")
                    break;

                await Task.Delay(TimeSpan.FromSeconds(delaySegundos));
                intentos++;
            }

            if (estado == "Running")
            {
                _logger.LogWarning("[MONITOREO] Tiempo agotado esperando job para ID: {id}. Estado sigue en 'Running'.");
                estado = "Timeout";
            }

            return estado;
        }

        public async Task<bool> TestMonitorAndNotifyAsync(string id, string entorno)
        {
            _logger.LogInformation("[PRUEBA-MONITOREO] Ejecutando monitoreo + notificación para ID: {id}", id);

            try
            {
                var estadoFinal = await MonitorJobRunUntilCompleteAsync(id);
                _logger.LogInformation("[PRUEBA-MONITOREO] Estado final del job: {estadoFinal}", estadoFinal);

                // Pasar estado final como mensaje si es error
                string? errorMsg = estadoFinal.Equals("Failed", StringComparison.OrdinalIgnoreCase) ||
                                   estadoFinal.Equals("Stopped", StringComparison.OrdinalIgnoreCase)
                                   ? estadoFinal
                                   : null;

                var notifyResult = await NotifyOutSystemsProcessFinishedAsync(id, entorno, errorMsg);

                if (notifyResult.Success)
                {
                    _logger.LogInformation("[PRUEBA-MONITOREO] Notificación enviada correctamente a OutSystems.");
                    return estadoFinal.Equals("Completed", StringComparison.OrdinalIgnoreCase);
                }
                else
                {
                    _logger.LogWarning("[PRUEBA-MONITOREO] Falló la notificación a OutSystems: {mensaje}", notifyResult.Message);
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[PRUEBA-MONITOREO] Excepción al monitorear y notificar para ID: {id}", id);
                return false;
            }
        }

    }
}
