using Amazon.Lambda.Core;
using Amazon.Lambda.Serialization.SystemTextJson;
using System.Text.Json;
using Amazon.Lambda.CloudWatchEvents;
using ImageProcessing.Model;
using ImageProcessing.Services;

[assembly: LambdaSerializer(typeof(DefaultLambdaJsonSerializer))]

namespace ImageProcessing
{
    public class Function
    {
        public void Handler(CloudWatchEvent<ApplicationEvent> applicationEvent, ILambdaContext context)
        {
            try
            {
                context.Logger.LogLine($"Lambda received event ApplicationId: {applicationEvent.Detail.ApplicationId}, Path1: {applicationEvent.Detail.Path1}, Path2: {applicationEvent.Detail.Path2}");
                string s3BucketName = Environment.GetEnvironmentVariable("S3BucketName") ?? throw new InvalidOperationException("S3BucketName environment variable is required");
                float similarityThreshold = float.Parse(Environment.GetEnvironmentVariable("SimilarityScoreThreshold") ?? throw new InvalidOperationException("SimilarityScoreThreshold environment variable is required"));

                using var _imageProcessor = new ImageProcessor(context.Logger);

                // Using the _imageProcessor service:
                // - Invoke CompareFaces, put results in variable named _result
                // - Invoke PublishEvent, passing in the _result

            }
            catch (Exception ex)
            {
                context.Logger.LogError("Error in FunctionHandler: " + ex.Message);
                throw;
            }
        }
    }
}