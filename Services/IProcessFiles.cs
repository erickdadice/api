using Azure.Storage.Blobs;
using Microsoft.AspNetCore.Http;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using FastApiProcessor.Models;

namespace FastApiProcessor.Services
{
    public interface IProcessFiles
    {
        // -------------------- Subida de archivos --------------------
        Task<List<string>> ProcessAndUploadFilesAsync(IFormFile[] files, string guid);
        Task<string> UploadFilesWithMetadataAsync(IFormFile file1, IFormFile file2, IFormFile file3,
            string cod, string titulo, int ver, int sinAnul, int opcion, int modelo, string entorno);
        Task<string> UploadAndStoreMetadataAsync(IFormFile file1, IFormFile file2, IFormFile file3,
            string cod, string titulo, int ver, int sinAnul, int opcion, int modelo, string entorno);
        Task<string> FullProcessAsync(IFormFile file1, IFormFile file2, IFormFile file3,
            string cod, string titulo, int ver, int sinAnul, int opcion, int modelo, string entorno);
        Task<string> UploadFilesBase64WithMetadataAsync(UploadMetadataBase64Request request);

        // -------------------- SAS con User Delegation Key --------------------
        Task<List<string>> GenerateUserDelegationSasUrlsAsync(string guid);
        Task<string> GenerateUserDelegationSasUrlForFileAsync(string guid, string fileName);

        // -------------------- Acceso directo --------------------
        BlobClient GetBlobClient(string guid, string fileName);
        Task<BinaryFileResult?> GetFileBinaryAsync(string guid, string fileName);
        Task<MemoryStream> DownloadAllFilesAsZipAsync(string guid);
        Task<NotifyResult> NotifyOutSystemsProcessFinishedAsync(string id, string entorno, string? errorOnAzure = null);




        // -------------------- Eliminación --------------------
        Task<bool> DeleteFileAsync(string guid, string fileName);
        Task<List<string>> DeleteAllFilesByIdAsync(string guid);

        // -------------------- Utilidades --------------------
        List<string> ListAllIds();
        List<string> ListFileNamesById(string guid);
        List<string> ListIdsByDate(string yyyyMMdd);
        Task<List<object>> GenerateUserDelegationSasUrlsForOutputAsync(string id);


        // -------------------- Control de jobs y confirmación --------------------
        Task<bool> LaunchJobOnlyAsync(string id);
        Task<bool> UpdateStatusAsync(string id, string status);
        Task ConfirmProcessExternallyAsync(string id);
        Task<string> TriggerAzureContainerJobAsync(string id, string entorno);
        Task<bool> TestMonitorAndNotifyAsync(string id, string entorno);
        Task<string> MonitorJobRunUntilCompleteAsync(string id);
      





        // -------------------- Nuevas funcionalidades de monitoreo y metadata --------------------
        Task<string> ReadMetadataParamStringAsync(string id); //  Para extraer línea RSCRIPTPARAMS
        Task<string> GetJobStatusAsync(string id); // para exponer estado del job desde logs o Azure API


    }
}

