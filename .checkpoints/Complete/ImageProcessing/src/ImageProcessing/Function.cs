using Amazon.Lambda.Core;
using Amazon.Lambda.RuntimeSupport;
using Amazon.Lambda.Serialization.SystemTextJson;
using Amazon.EventBridge.Model;
using Amazon.EventBridge;
using System.Text.Json;
using Amazon.Lambda.CloudWatchEvents;
using ImageProcessing.Model;
using Amazon.Rekognition.Model;
using Amazon.Rekognition;

ILambdaLogger logger;

var handler = (CloudWatchEvent<ApplicationEvent> applicationEvent, ILambdaContext context) =>
{
    logger = context.Logger;
    logger.LogLine($"Lambda received event ApplicationId: {applicationEvent.Detail.ApplicationId}, Path1: {applicationEvent.Detail.Path1}, Path2: {applicationEvent.Detail.Path2}");
    string s3BucketName = Environment.GetEnvironmentVariable("S3BucketName");
    float similarityThreshold = float.Parse(Environment.GetEnvironmentVariable("SimilarityScoreThreshold"));

    // Call ProcessImage
    ProcessImage(applicationEvent.Detail.ApplicationId, s3BucketName, applicationEvent.Detail.Path1, applicationEvent.Detail.Path2, similarityThreshold);
};

/// <summary>
/// Use Rekognition to do a face comparison between 2 pictures stored in the same S3 bucket
/// Evaluate the face comparison result based on a similary threshold score
/// This method does not return anything, but it will publish the face comparison and evaluation results into EventBridge event bus
/// The event detail contains ApplicationId, path1, path2, docType = 3, Status and Remarks
/// Status can take the value of either2 or 10, where:
///     2 is a catch all failure while doing face comparison, including when the face in the two images do not match or below the similarity threshold score
///     10 means the face in the two images match with similarity score above the set thredhold
/// Remarks is a string value that will be used to provide additional information about the failure or success
/// </summary>
/// <param name="applicationId">Application reference Number</param>
/// <param name="s3BucketName">Name of the S3 bucket where the images are stored in</param>
/// <param name="path1">Key of the image 1 in S3 bucket to be used for comparison, including any prefix</param>
/// <param name="path2">Key of the image 2 in S3 bucket to be used for comparison, including any prefix</param>
/// <param name="similarityThreshold">Similarity threshold score</param>
void ProcessImage(string applicationId, string s3BucketName, string path1, string path2, float similarityThreshold)
{
    using (var rekognitionClient = new AmazonRekognitionClient())
    {
        var compareFacesRequest = new CompareFacesRequest()
        {
            SourceImage = new Image() { S3Object = new S3Object() { Bucket = s3BucketName, Name = path1 } },
            TargetImage = new Image() { S3Object = new S3Object() { Bucket = s3BucketName, Name = path2 } },
            SimilarityThreshold = similarityThreshold
        };
        var compareFacesResponse = rekognitionClient.CompareFacesAsync(compareFacesRequest).Result;

        PublishEvent(new
        {
            ApplicationId = applicationId,
            Path1 = path1,
            Path2 = path2,
            DocType = (int)DocumentType.SELFIE,
            Status = compareFacesResponse.FaceMatches.Count > 0 ? 10 : 2,
            Remarks = compareFacesResponse.FaceMatches.Count > 0 
                ? $"Face matches with similarity score of {compareFacesResponse.FaceMatches.Max(f => f.Similarity)}%" 
                : "Faces do not match"
        }); 
    }
}

void PublishEvent(object payload, string eventNameOverride = "")
{
    // check if eventName is nullor empty, if it is, take the value from Environment.GetEnvironmentVariable("EventName")
    // if it is still null or empty, set it to "application.image.processed"
    var eventName = (Environment.GetEnvironmentVariable("EventName") ?? "application.image.processed");
    eventName = string.IsNullOrEmpty(eventNameOverride) ? eventName : eventNameOverride;
    var eventDetail = JsonSerializer.Serialize(payload);
    using (AmazonEventBridgeClient eventBridgeClient = new AmazonEventBridgeClient())
    {
        var eventPublish = eventBridgeClient.PutEventsAsync(new PutEventsRequest()
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
    }
    logger.LogLine($"Event {eventName} published: {eventDetail}");
}

// Build the Lambda runtime client passing in the handler to call for each
// event and the JSON serializer to use for translating Lambda JSON documents
// to .NET types.
await LambdaBootstrapBuilder.Create(handler, new DefaultLambdaJsonSerializer())
        .Build()
        .RunAsync();