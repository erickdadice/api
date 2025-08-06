using System;
using System.Security.Cryptography;
using System.Text;

namespace FastApiProcessor.Utils
{
    public static class IdGenerator
    {
        /// <summary>
        /// Genera un ID único basado en la fecha y un hash SHA256.
        /// Ejemplo: 20250721A1B2C3D4E5
        /// </summary>
        /// <returns>ID en formato YYYYMMDD + primeros 10 caracteres del hash.</returns>
        public static string GenerateUniqueId()
        {
            using var sha256 = SHA256.Create();
            var now = DateTime.UtcNow;

            // Convierte el timestamp en bytes y calcula el hash SHA256
            var inputBytes = Encoding.UTF8.GetBytes(now.Ticks.ToString());
            var hashBytes = sha256.ComputeHash(inputBytes);

            // Extrae los primeros 10 caracteres hexadecimales del hash
            var hashPart = BitConverter.ToString(hashBytes).Replace("-", "").Substring(0, 10);

            // Combina con la fecha actual en formato YYYYMMDD
            return $"{now:yyyyMMdd}{hashPart}";
        }
    }
}
