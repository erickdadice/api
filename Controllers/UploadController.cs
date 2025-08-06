using FastApiProcessor.Models;
using FastApiProcessor.Services;
using FastApiProcessor.Utils;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace FastApiProcessor.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class UploadController : ControllerBase
    {
        private readonly IProcessFiles _processFiles;
        private readonly ILogger<UploadController> _logger;

        public UploadController(IProcessFiles processFiles, ILogger<UploadController> logger)
        {
            _processFiles = processFiles;
            _logger = logger;
        }


        [HttpGet("downloadAllFilesZip/{guid}")]
        [Tags("Descarga de Archivos")]
        public async Task<IActionResult> DownloadAllFilesAsZip(string guid)
        {
            try
            {
                var zipStream = await _processFiles.DownloadAllFilesAsZipAsync(guid);
                if (zipStream.Length == 0)
                    return NotFound("No se encontraron archivos para ese ID.");

                var contentType = "application/zip";
                var fileName = $"archivos_{guid}.zip";
                return File(zipStream, contentType, fileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al descargar los archivos en ZIP.");
                return StatusCode(500, "Error interno al generar el ZIP.");
            }
        }

        [HttpGet("getFileUrlsById/{guid}")]
        [Tags("Descarga de Archivos")]
        public async Task<IActionResult> GetFileUrlsById(string guid)
        {
            var urls = await _processFiles.GenerateUserDelegationSasUrlsAsync(guid);
            return Ok(urls);
        }



        [HttpGet("getFileBinary/{guid}/{filename}")]
        [Tags("Descarga de Archivos")]
        public async Task<IActionResult> GetFileBinary(string guid, string filename)
        {
            try
            {
                var result = await _processFiles.GetFileBinaryAsync(guid, filename);
                if (result == null)
                    return NotFound("Archivo no encontrado.");

                return File(result.Content, result.ContentType, result.FileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener el archivo binario.");
                return StatusCode(500, "Error interno al recuperar el archivo.");
            }
        }

        // -------------------- ELIMINACIÓN DE ARCHIVOS --------------------

        [HttpDelete("deleteFileById/{guid}/{filename}")]
        [Tags("Eliminación de Archivos")]
        public async Task<IActionResult> DeleteFileById(string guid, string filename)
        {
            var eliminado = await _processFiles.DeleteFileAsync(guid, filename);
            return eliminado ? Ok("Archivo eliminado exitosamente.") : NotFound("Archivo no encontrado.");
        }

        [HttpDelete("deleteFilesById/{guid}")]
        [Tags("Eliminación de Archivos")]
        public async Task<IActionResult> DeleteFilesById(string guid)
        {
            var resultado = await _processFiles.DeleteAllFilesByIdAsync(guid);
            return resultado.Count == 0
                ? NotFound("No se encontraron archivos.")
                : Ok(new { Mensaje = "Archivos eliminados.", Cantidad = resultado.Count });
        }

        // -------------------- CONSULTA DE ARCHIVOS --------------------



        [HttpPost("uploadBase64")]
        [Tags("Procesamiento y Jobs")]
        public async Task<IActionResult> UploadFilesFromBase64Json([FromBody] UploadMetadataBase64Request request)
        {
            if (request == null ||
                string.IsNullOrWhiteSpace(request.Fichero1Contenido) ||
                string.IsNullOrWhiteSpace(request.Fichero2Contenido) ||
                string.IsNullOrWhiteSpace(request.Fichero3Contenido))
            {
                _logger.LogWarning("❗ Request inválido: falta contenido base64 en uno o más archivos.");
                return BadRequest("Archivos codificados base64 requeridos.");
            }

            string? id = null;

            try
            {
                _logger.LogInformation(" Iniciando UploadFilesBase64WithMetadataAsync...");
                id = await _processFiles.UploadFilesBase64WithMetadataAsync(request);
                _logger.LogInformation(" Archivos base64 subidos. ID generado: {id}", id);

                //  Ejecutar el job en background
                _ = Task.Run(async () =>
                {
                    try
                    {
                        _logger.LogInformation("🚀 Ejecutando job en segundo plano para ID: {id}", id);
                        await _processFiles.TriggerAzureContainerJobAsync(id, request.Entorno);
                    }
                    catch (Exception bgEx)
                    {
                        _logger.LogError(bgEx, "Error al ejecutar job en background para ID: {id}", id);
                    }
                });

                // 🔹 Respuesta inmediata
                return Ok(new
                {
                    id,
                    mensaje = "Proceso iniciado correctamente (archivos base64). La ejecución del job continúa en segundo plano."
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, " Excepción en uploadBase64. ID generado (si existe): {id}", id ?? "N/A");
                return StatusCode(500, "Error al ejecutar el proceso.");
            }
        }


/*
        [HttpPost("fullProcessAndTriggerJob")]
        [Tags("Procesamiento y Jobs")]
        public async Task<IActionResult> FullProcessAndTriggerJob([FromForm] UploadMetadataRequest request)
        {
            _logger.LogInformation(" Entrando a FullProcessAndTriggerJob. Archivos recibidos: {count}", Request.Form.Files.Count);

            if (Request.Form.Files.Count != 3)
                return BadRequest("Se requieren exactamente 3 archivos adjuntos.");

            var f1 = Request.Form.Files[0];
            var f2 = Request.Form.Files[1];
            var f3 = Request.Form.Files[2];

            _logger.LogInformation("📎 Nombres de archivos: {f1}, {f2}, {f3}", f1.FileName, f2.FileName, f3.FileName);

            try
            {
                // 1️ Subir archivos y generar ID
                var id = await _processFiles.FullProcessAsync(f1, f2, f3,
                    request.CodigoExamen, request.TituloExamen, request.VersionActual,
                    request.SinAnuladas, request.Opcion, request.Modelo, request.Entorno);

                _logger.LogInformation(" ID generado por FullProcessAsync: {id}", id);

                // 2️ Ejecutar en background el Job sin esperar a que finalice
                _ = Task.Run(async () =>
                {
                    try
                    {
                        _logger.LogInformation(" Ejecutando job en segundo plano para ID: {id}", id);
                        await _processFiles.TriggerAzureContainerJobAsync(id, request.Entorno);
                    }
                    catch (Exception bgEx)
                    {
                        _logger.LogError(bgEx, "Error al ejecutar job en background para ID: {id}", id);
                    }
                });

                // 3️ Devolver respuesta inmediata con el ProcessId
                return Ok(new
                {
                    id,
                    mensaje = "Proceso iniciado correctamente. La ejecución del job continúa en segundo plano."
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, " Error en el proceso completo.");
                return StatusCode(500, "Error al ejecutar el proceso.");
            }
        }
*/

    }
}

