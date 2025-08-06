namespace FastApiProcessor.Models
{
    public class BinaryFileResult
    {
        public byte[] Content { get; set; }
        public string ContentType { get; set; }
        public string FileName { get; set; }

        public BinaryFileResult(byte[] content, string contentType, string fileName)
        {
            Content = content;
            ContentType = contentType;
            FileName = fileName;
        }
    }
}
