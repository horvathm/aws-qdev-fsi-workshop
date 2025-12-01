namespace DocumentProcessing.Model
{
    public class ApplicationEvent
    {
        public string ApplicationId { get; set; } = null!;
        public DocumentType DocType { get; set; }
        public string Path { get; set; } = null!;
        public string? UtcTime { get; set; }
    }

    public enum DocumentType
    {
        INCOMESTATEMENT = 1,
        IDENTITYDOCUMENT = 2,
        SELFIE = 3
    }
}