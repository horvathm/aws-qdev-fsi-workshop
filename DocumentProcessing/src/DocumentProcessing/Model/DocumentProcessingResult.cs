namespace DocumentProcessing.Model
{
    public class DocumentProcessingResult
    {
        public string ApplicationId { get; set; } = null!;
        public string Path { get; set; } = null!;
        public int DocType { get; set; }
        public int Status { get; set; }
        public string? Remarks { get; set; }
    }
}
