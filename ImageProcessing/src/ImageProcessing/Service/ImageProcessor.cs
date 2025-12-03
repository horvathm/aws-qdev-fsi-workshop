using ImageProcessing.Model;
using System.Text.Json;
using Amazon.Lambda.Core;
using Amazon.Rekognition;
using Amazon.Rekognition.Model;
using Amazon.EventBridge.Model;
using Amazon.EventBridge;

namespace ImageProcessing.Services
{
    public class ImageProcessor : IImageProcessor, IDisposable
    {
        private readonly ILambdaLogger _logger;
        private readonly IAmazonRekognition _rekognitionClient;
        private readonly IAmazonEventBridge _eventBridgeClient;

        public ImageProcessor(ILambdaLogger logger) : this(logger, new AmazonRekognitionClient(), new AmazonEventBridgeClient())
        {
        }

        public ImageProcessor(ILambdaLogger logger, IAmazonRekognition rekognitionClient, IAmazonEventBridge eventBridgeClient)
        {
            _logger = logger;
            _rekognitionClient = rekognitionClient;
            _eventBridgeClient = eventBridgeClient;
        }

        /// <summary>
        /// Compare two images stored in Amazon S3 using Amazon Rekognition
        /// Returns FaceComparisonResult:
        ///    - With corresponding ApplicationId and Paths from this function arguments
        ///    - Status = 10 if FaceMatches.Count > 0, else Status=2
        ///    - DocType being (int) value of DocumentType.SELFIE since this processes selfie
        ///    - Remarks indicates "Face matches with similarity score of {Max(f.Similarity)}%" upon match
        /// </summary>
        /// <param name="applicationId">Application reference Number</param>
        /// <param name="s3BucketName">Name of the S3 bucket where the images are stored in</param>
        /// <param name="path1">Key of the image 1 in S3 bucket, including any prefix</param>
        /// <param name="path2">Key of the image 2 in S3 bucket, including any prefix</param>
        /// <param name="similarityThreshold">Threshold to determine if the images are considered match or no match</param>
        /// <returns>FaceComparisonResult object containing the outcome of the face comparison</returns>
        public FaceComparisonResult CompareFaces(string applicationId, string s3BucketName, string path1, string path2, float similarityThreshold)
        {
            var compareFacesRequest = new CompareFacesRequest()
            {
                SourceImage = new Image() { S3Object = new S3Object() { Bucket = s3BucketName, Name = path1 } },
                TargetImage = new Image() { S3Object = new S3Object() { Bucket = s3BucketName, Name = path2 } },
                SimilarityThreshold = similarityThreshold
            };
            var compareFacesResponse = _rekognitionClient.CompareFacesAsync(compareFacesRequest).Result;

            return new FaceComparisonResult()
            {
                ApplicationId = applicationId,
                Path1 = path1,
                Path2 = path2,
                DocType = (int)DocumentType.SELFIE,
                Status = compareFacesResponse.FaceMatches.Count > 0 ? 10 : 2,
                Remarks = compareFacesResponse.FaceMatches.Count > 0 
                    ? $"Face matches with similarity score of {compareFacesResponse.FaceMatches.Max(f => f.Similarity)}%" 
                    : "Faces do not match"
            };
        }

        public void PublishEvent(object payload, string? eventNameOverride = "")
        {
            // check if eventName is null or empty, if it is, take the value from Environment.GetEnvironmentVariable("EventName")
            // if it is still null or empty, set it to "application.image.processed"
            var eventName = (Environment.GetEnvironmentVariable("EventName") ?? "application.image.processed");
            eventName = string.IsNullOrEmpty(eventNameOverride) ? eventName : eventNameOverride;
            var eventDetail = JsonSerializer.Serialize(payload);
            var eventPublish = _eventBridgeClient.PutEventsAsync(new PutEventsRequest()
            {
                Entries = new List<PutEventsRequestEntry>()
                    {
                        new PutEventsRequestEntry()
                        {
                            EventBusName = Environment.GetEnvironmentVariable("EventbusName"),
                            Source = Environment.GetEnvironmentVariable("ServiceName"),
                            Detail = eventDetail,
                            DetailType = eventName,
                            Time = DateTime.UtcNow
                        }
                    }
            }).Result;
            _logger.LogLine($"Event {eventName} published: {eventDetail}");
        }

        public void Dispose()
        {
            _rekognitionClient?.Dispose();
            _eventBridgeClient?.Dispose();
        }
    }
}