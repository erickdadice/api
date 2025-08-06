using System.ComponentModel.DataAnnotations;

public class UploadMetadataBase64Request
{
    [Required(ErrorMessage = "El campo CodigoExamen es obligatorio.")]
    public string CodigoExamen { get; set; } = string.Empty;

    [Required(ErrorMessage = "El campo TituloExamen es obligatorio.")]
    public string TituloExamen { get; set; } = string.Empty;

    [Required(ErrorMessage = "El campo VersionActual es obligatorio.")]
    public int VersionActual { get; set; }

    [Required(ErrorMessage = "El campo SinAnuladas es obligatorio.")]
    public int SinAnuladas { get; set; }

    [Required(ErrorMessage = "El campo Opcion es obligatorio.")]
    public int Opcion { get; set; }

    [Required(ErrorMessage = "El campo Modelo es obligatorio.")]
    public int Modelo { get; set; }

    [Required(ErrorMessage = "El campo Entorno es obligatorio.")]
    public string Entorno { get; set; } = string.Empty;

    [Required(ErrorMessage = "Debe incluir el nombre del fichero 1.")]
    public string Fichero1Nombre { get; set; } = string.Empty;

    [Required(ErrorMessage = "Debe incluir el contenido en base64 del fichero 1.")]
    public string Fichero1Contenido { get; set; } = string.Empty;

    [Required(ErrorMessage = "Debe incluir el nombre del fichero 2.")]
    public string Fichero2Nombre { get; set; } = string.Empty;

    [Required(ErrorMessage = "Debe incluir el contenido en base64 del fichero 2.")]
    public string Fichero2Contenido { get; set; } = string.Empty;

    [Required(ErrorMessage = "Debe incluir el nombre del fichero 3.")]
    public string Fichero3Nombre { get; set; } = string.Empty;

    [Required(ErrorMessage = "Debe incluir el contenido en base64 del fichero 3.")]
    public string Fichero3Contenido { get; set; } = string.Empty;
}


