using ImageProcessing.Model;

namespace ImageProcessing.Services
{
    public interface IImageProcessor : IDisposable
    {
        FaceComparisonResult CompareFaces(string applicationId, string s3BucketName, string path1, string path2, float similarityThreshold);
        void PublishEvent(object payload, string? eventNameOverride);
    }
}