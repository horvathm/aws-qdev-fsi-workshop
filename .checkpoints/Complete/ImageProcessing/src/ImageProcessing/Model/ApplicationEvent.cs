namespace ImageProcessing.Model
{
    internal class ApplicationEvent
    {
        public string ApplicationId { get; set; }
        public string Path1 { get; set; }
        public string Path2 { get; set; }
        public string UtcTime { get; set; }
    }

    internal enum DocumentType
    {
        INCOMESTATEMENT = 1,
        IDENTITYDOCUMENT = 2,
        SELFIE = 3
    }
}