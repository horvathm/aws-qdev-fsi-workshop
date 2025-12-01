namespace ImageProcessing.Model
{
    public class FaceComparisonResult
    {
        public string ApplicationId { get; set; } = null!;
        public string Path1 { get; set; } = null!;
        public string Path2 { get; set; } = null!;
        public int DocType { get; set; }
        public int Status { get; set; }
        public string? Remarks { get; set; }
    }
}
