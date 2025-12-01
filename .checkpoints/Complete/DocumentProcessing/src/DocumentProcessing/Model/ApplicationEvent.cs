namespace DocumentProcessing.Model
{
    internal class ApplicationEvent
    {
        public string ApplicationId { get; set; }
        public DocumentType DocType { get; set; }
        public string Path { get; set; }
        public string UtcTime { get; set; }
    }

    internal enum DocumentType
    {
        INCOMESTATEMENT = 1,
        IDENTITYDOCUMENT = 2,
        SELFIE = 3
    }
}