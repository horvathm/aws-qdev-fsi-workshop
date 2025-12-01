using Amazon.Lambda.Core;
using Amazon.Lambda.Serialization.SystemTextJson;
using Amazon.Lambda.CloudWatchEvents;
using ImageProcessing.Model;
using ImageProcessing.Services;
using System.Diagnostics;
using Amazon.Rekognition;
using Amazon.EventBridge;

// Assembly attribute to enable the Lambda function's JSON input to be converted into a .NET class.
[assembly: LambdaSerializer(typeof(DefaultLambdaJsonSerializer))]

namespace ImageProcessing
{
    public class Function : IDisposable
    {
        private readonly ActivitySource _tracer = new ActivitySource("ImageProcessing");
        private readonly ImageProcessor _imageProcessor;
        private readonly AmazonRekognitionClient? _rekognitionClient;
        private readonly AmazonEventBridgeClient? _eventBridgeClient;
        private bool _disposed = false;

        public Function()
        {
            _rekognitionClient = new AmazonRekognitionClient();
            _eventBridgeClient = new AmazonEventBridgeClient();
            _imageProcessor = new ImageProcessor(_rekognitionClient, _eventBridgeClient);
        }

        // Constructor for testing
        public Function(ImageProcessor imageProcessor)
        {
            _imageProcessor = imageProcessor;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed && disposing)
            {
                _rekognitionClient?.Dispose();
                _eventBridgeClient?.Dispose();
                _tracer?.Dispose();
                _disposed = true;
            }
        }

        public async Task Handler(CloudWatchEvent<ApplicationEvent> applicationEvent, ILambdaContext context)
        {
            using var activity = _tracer.StartActivity("FunctionHandler", ActivityKind.Server);
            activity?.SetTag("ApplicationId", applicationEvent.Detail.ApplicationId);
            activity?.SetTag("Path1", applicationEvent.Detail.Path1);
            activity?.SetTag("Path2", applicationEvent.Detail.Path2);
            
            try
            {
                context.Logger.LogLine($"Lambda received event ApplicationId: {applicationEvent.Detail.ApplicationId}, Path1: {applicationEvent.Detail.Path1}, Path2: {applicationEvent.Detail.Path2}");
                var traceId = Environment.GetEnvironmentVariable("_X_AMZN_TRACE_ID");
                context.Logger.LogLine($"Logging trace id: {traceId}");

                string s3BucketName = Environment.GetEnvironmentVariable("S3BucketName");
                float similarityThreshold = float.Parse(Environment.GetEnvironmentVariable("SimilarityScoreThreshold"));

                var result = _imageProcessor.CompareFaces(
                    applicationEvent.Detail.ApplicationId, 
                    s3BucketName, 
                    applicationEvent.Detail.Path1, 
                    applicationEvent.Detail.Path2, 
                    similarityThreshold
                );

                _imageProcessor.PublishEvent(result, null);
                
                context.Logger.LogLine($"Face comparison completed for ApplicationId: {applicationEvent.Detail.ApplicationId}, Status: {result.Status}, Remarks: {result.Remarks}");
                activity?.SetStatus(ActivityStatusCode.Ok);
            }
            catch (Exception ex)
            {
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                context.Logger.LogError(ex, "Error in FunctionHandler");
                throw;
            }
        }


    }
}

