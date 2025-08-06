namespace FastApiProcessor.Models
{
    public class NotifyResult
    {
        public bool Success { get; set; }
        public string Message { get; set; }

        public NotifyResult(bool success, string message)
        {
            Success = success;
            Message = message;
        }
    }
}

