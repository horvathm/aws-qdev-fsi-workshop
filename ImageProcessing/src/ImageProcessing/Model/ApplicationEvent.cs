namespace ImageProcessing.Model
{
    public class ApplicationEvent
    {
        public string ApplicationId { get; set; } = null!;
        public string Path1 { get; set; } = null!;
        public string Path2 { get; set; } = null!;
        public string? UtcTime { get; set; }
    }

    public enum DocumentType
    {
        INCOMESTATEMENT = 1,
        IDENTITYDOCUMENT = 2,
        SELFIE = 3
    }
}