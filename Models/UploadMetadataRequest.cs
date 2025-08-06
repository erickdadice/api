using System.ComponentModel.DataAnnotations;
using Swashbuckle.AspNetCore.Annotations;


public class UploadMetadataRequest
{
    [Required]
    public IFormFile Fichero1 { get; set; } = default!;
    [Required]
    public IFormFile Fichero2 { get; set; } = default!;
    [Required]
    public IFormFile Fichero3 { get; set; } = default!;

    [Required]
    public string CodigoExamen { get; set; } = string.Empty;

    [Required]
    public string TituloExamen { get; set; } = string.Empty;

    public int VersionActual { get; set; }
    public int SinAnuladas { get; set; }
    public int Opcion { get; set; }
    public int Modelo { get; set; }

    [Required]
    public string Entorno { get; set; } = string.Empty;

    [SwaggerSchema(ReadOnly = true)]
    public string Id { get; set; } = string.Empty;

    [SwaggerSchema(ReadOnly = true)]
    public string Fichero1Nombre { get; set; } = string.Empty;
    [SwaggerSchema(ReadOnly = true)]
    public string Fichero2Nombre { get; set; } = string.Empty;
    [SwaggerSchema(ReadOnly = true)]
    public string Fichero3Nombre { get; set; } = string.Empty;
}

