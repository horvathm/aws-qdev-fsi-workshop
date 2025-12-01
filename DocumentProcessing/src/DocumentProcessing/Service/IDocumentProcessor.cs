using DocumentProcessing.Model;

namespace DocumentProcessing.Services
{
    public interface IDocumentProcessor : IDisposable
    {
        DocumentProcessingResult ProcessDocument(string applicationId, DocumentType docType, string s3BucketName, string path, string query, decimal minIncome);
        void PublishEvent(object payload, string? eventNameOverride);
    }
}