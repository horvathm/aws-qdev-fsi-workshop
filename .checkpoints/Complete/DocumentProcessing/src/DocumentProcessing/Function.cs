using Amazon.Lambda.Core;
using Amazon.Lambda.RuntimeSupport;
using Amazon.Lambda.Serialization.SystemTextJson;
using Amazon.Lambda.CloudWatchEvents;
using Amazon.Textract.Model;
using Amazon.Textract;
using Amazon.EventBridge.Model;
using DocumentProcessing.Model;
using Amazon.EventBridge;
using System.Text.Json;

ILambdaLogger logger;

// COMPLETED CODE: Lambda Handler
var handler = (CloudWatchEvent<ApplicationEvent> applicationEvent, ILambdaContext context) =>
{
    logger = context.Logger;
    logger.LogLine($"Lambda received event ApplicationId: {applicationEvent.Detail.ApplicationId}, DocType: {applicationEvent.Detail.DocType}, Path: {applicationEvent.Detail.Path}");

    string s3BucketName = Environment.GetEnvironmentVariable("S3BucketName");
    decimal minIncome = decimal.Parse(Environment.GetEnvironmentVariable("MinIncome"));

    string query;
    if (applicationEvent.Detail.DocType == DocumentType.INCOMESTATEMENT) query = Environment.GetEnvironmentVariable("IncomeQuery");
    else if (applicationEvent.Detail.DocType == DocumentType.IDENTITYDOCUMENT) query = Environment.GetEnvironmentVariable("IdDocQuery");
    else
    {
        logger.LogWarning($"There is no extraction implementation concerning DocType: {applicationEvent.Detail.DocType}");
        return;
    }

    ProcessDocument(applicationEvent.Detail.ApplicationId, applicationEvent.Detail.DocType, s3BucketName, applicationEvent.Detail.Path, query, minIncome);
};

/// <summary>
/// Use Textract to query a document stored in S3
/// Evaluate the text extraction answers based on the query against business rule depending on the docType
/// For docType = INCOMESTATEMENT, the business rule is to ascertain extracted income is equal to or greater than minIncome
/// For docType = IDENTITYDOCUMENT, the business rule is to ascertain that the extracted expiry date is in the future
/// This method does not return anything, but it will publish the extraction and evaluation results into EventBridge event bus
/// Each published event will have DetailType = "application.document.processed"
/// The event detail contains applicationId, integer value of the docType being processed, path, Status and Remarks
/// Status can take the value of either 1, 2 or 10, where:
///     1 means failure against business rule, such as when document is expired or income is below minIncome
///     2 means failure in extraction or parsing, such as when no answer is found against the query or multiple answers are found, or when there is data type conversion issue
///     10 means success, such as when document is valid and passes business rule
/// Remarks is a string value that will be used to provide additional information about the failure or success
/// </summary>
/// <param name="ApplicationId">Application reference Number</param>
/// <param name="docType">Document type, this method can only handle INCOMESTATEMENT or IDENTITYDOCUMENT</param>
/// <param name="s3BucketName">Name of the S3 bucket where the document is stored in</param>
/// <param name="path">Key of the document in S3 bucket, including any prefix</param>
/// <param name="query">Query to pass to Textract</param>
/// <param name="minIncome">Minimum income to be evaluated if document type is INCOMESTATEMENT</param>
void ProcessDocument(string applicationId, DocumentType docType, string s3BucketName, string path, string query, decimal minIncome)
{
    var queriesConfig = new QueriesConfig();
    queriesConfig.Queries ??= new List<Query>();
    string[] queries = [query];
    int i = 1;
    foreach (var queryText in queries)
    {
        queriesConfig.Queries.Add(new Query()
        {
            Alias = $"Query {i}",
            Pages = new List<string>() { "*" },
            Text = queryText
        });
    }

    //invoke Textract on the document in S3, put the analysis result into a variable called response
    AnalyzeDocumentResponse response = new AnalyzeDocumentResponse();
    using (var textractClient = new AmazonTextractClient())
    {
        response = textractClient.AnalyzeDocumentAsync(new AnalyzeDocumentRequest()
        {
            Document = new Document()
            {
                S3Object = new S3Object()
                {
                    Bucket = s3BucketName,
                    Name = path
                }
            },
            // Replace FeatureTypes = new List<string>() { "TABLES" }, with
            FeatureTypes = new List<string>() { "QUERIES" },
            QueriesConfig = queriesConfig
        }).Result;
    }

    // Extract the query results from the Textract response into a variable called answers
    // I need only the response blocks containing query results.
    IEnumerable<Block> answers = response.Blocks.Where(b => b.BlockType == BlockType.QUERY_RESULT);

    // ensure that there is exactly 1 answer
    // else we will publish a failure event indicating no answer or multiple answers found and exit
    if (answers.Count() != 1)
    {
        PublishEvent(new
        {
            ApplicationId = applicationId,
            DocType = (int)docType,
            Status = 2,
            Remarks = "No answer or multiple answers found"
        });
        return;
    }

    // evaluate against business rule depending on the document type
    // if docType is DocumentType.INCOMESTATEMENT, try to parse the extracted income string as decimal based on the answer
    // If it can be parsed, publish an application.document.processed event
    // If the extracted income >= minIncome Status of event is 10, remarks say income meets criteria
    //    Otherwise Status of event is 1 and remarks say income does not meet criteria
    // If the answer can't be parsed as decimal, publish a failure event and remarks saying income is not a proper numeric format
    if (docType == DocumentType.INCOMESTATEMENT)
    {
        string extractedIncomeString = answers.First().Text;
        if (decimal.TryParse(extractedIncomeString, out decimal extractedIncome))
        {
            PublishEvent(new
            {
                ApplicationId = applicationId,
                DocType = (int)docType,
                Status = extractedIncome >= minIncome ? 10 : 1,
                Remarks = $"Income {extractedIncome} " + (extractedIncome >= minIncome ? "meets" : "does not meet") + " income criteria"
            });
        }
        else
        {
            logger.LogWarning($"Query {query} matches {extractedIncomeString} but is not a valid decimal");
            PublishEvent(new
            {
                ApplicationId = applicationId,
                DocType = (int)docType,
                Status = 2, // pass for human review
                Remarks = $"Income is not proper numeric format: {extractedIncomeString}"
            });
        }
    }

    // if docType is DocumentType.IDENTITYDOCUMENT, try to parse the extracted expiry date string as DateTime based on the answer
    // If it can be parsed, publish an application.document.processed event
    // If the extracted date is in the future, Status of event is 10, otherwise status is 1
    // State expiry ddate in yyyy-MM-dd format in the remarks
    // If the answer can't be parsed as DateTime, publish a failure event and remarks saying income is not a proper date format
    if (docType == DocumentType.IDENTITYDOCUMENT)
    {
        string extractedExpiryDateString = answers.First().Text;
        if (DateTime.TryParse(extractedExpiryDateString, out DateTime extractedExpiryDate))
        {
            bool isStillValid = (extractedExpiryDate.CompareTo(DateTime.UtcNow) > 0);
            PublishEvent(new
            {
                ApplicationId = applicationId,
                DocType = (int)docType,
                Status = isStillValid ? 10 : 1,
                Remarks = "Document expiry date: " + extractedExpiryDate.ToString("yyyy-MM-dd")
            });
        }
        else
        {
            logger.LogWarning($"Query {query} matches {extractedExpiryDateString} but is not a known valid date format");
            PublishEvent(new
            {
                ApplicationId = applicationId,
                DocType = (int)docType,
                Status = 2, // pass for human review
                Remarks = $"Income is not recognized date format: {extractedExpiryDateString}"
            });
        }
    }
}

/// <summary>
/// Publishes an event to Amazon EventBridge with the specified event name and payload.
/// if eventNameOverride is not specified, it will take from EventName environment variable
/// if there is no EventName environment variable, it will be defaulted to application.document.processed
/// The event will be published to "EventbusName" that is configured in Lambda environment variable 
/// The event source will take the value of what's configured in "ServiceName" Lambda environment variable 
/// Time of event will always be set to current utc time
/// At the end it will use logger to log a message indicating document processed and event published
/// </summary>
/// <param name="payload">The object containing the event data. This will be serialized to JSON and sent as the event Detail.</param>
/// <param name="eventNameOverride">The type/name of the event being published. This will be used as the DetailType in EventBridge.</param>
void PublishEvent(object payload, string eventNameOverride = "")
{
    var eventName = (Environment.GetEnvironmentVariable("EventName") ?? "application.document.processed");
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