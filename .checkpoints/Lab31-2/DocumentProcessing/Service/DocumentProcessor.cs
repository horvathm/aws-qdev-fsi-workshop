using DocumentProcessing.Model;
using Amazon.Lambda.Core;
using Amazon.Textract.Model;
using Amazon.Textract;
using Amazon.EventBridge.Model;
using Amazon.EventBridge;
using System.Text.Json;
using System.Diagnostics;

namespace DocumentProcessing.Services
{
    public class DocumentProcessor : IDocumentProcessor, IDisposable
    {
        private readonly ILambdaLogger _logger;
        private readonly IAmazonTextract _textractClient;
        private readonly IAmazonEventBridge _eventBridgeClient;

        public DocumentProcessor(ILambdaLogger logger) : this(logger, new AmazonTextractClient(), new AmazonEventBridgeClient())
        {
        }

        public DocumentProcessor(ILambdaLogger logger, IAmazonTextract textractClient, IAmazonEventBridge eventBridgeClient)
        {
            _logger = logger;
            _textractClient = textractClient;
            _eventBridgeClient = eventBridgeClient;
        }

        /// <summary>
        /// Use Textract to query a document stored in S3
        /// Evaluate the text extraction answers based on the query against business rule depending on the docType
        /// Business rules evaluation:
        ///   - For docType = INCOMESTATEMENT, ascertain extracted income is equal to or greater than minIncome
        ///   - For docType = IDENTITYDOCUMENT, ascertain extracted expiry date is in the future
        /// This method returns as soon as the business rule evaluations are complete.
        /// The return DocumentProcessingResult object:
        ///   - Has the corresponding ApplicationId and Path from this function arguments
        ///   - DocType is the explicit int conversion of the docType argument
        ///   - Status is either 1, 2 or 10:
        ///     - 1 means failure against business rule, such as when document is expired or income is below minIncome
        ///     - 2 means failure in extraction or parsing, such as when no answer is found against the query or multiple answers are found, or when there is data type conversion issue
        ///     - 10 means success, such as when document is valid and passes business rule
        ///   - Remarks is a string value that will be used to provide additional information about the failure or success
        /// </summary>
        /// <param name="ApplicationId">Application reference Number</param>
        /// <param name="docType">Document type, this method can only handle INCOMESTATEMENT or IDENTITYDOCUMENT</param>
        /// <param name="s3BucketName">Name of the S3 bucket where the document is stored in</param>
        /// <param name="path">Key of the document in S3 bucket, including any prefix</param>
        /// <param name="query">Query to pass to Textract</param>
        /// <param name="minIncome">Minimum income to be evaluated if document type is INCOMESTATEMENT</param>
        /// <returns>DocumentProcessingResult object containing the outcome of the document processing</returns>
        /// <exception cref="Exception">
        /// The method returns as soon as business rule is evaluated
        /// Exception should only occur when there are issues invoking downstream services (e.g. Textract)
        /// Or that it has evaluated all business rules but is unable to reach a conclusion.
        /// </exception>
        public DocumentProcessingResult ProcessDocument(string applicationId, DocumentType docType, string s3BucketName, string path, string query, decimal minIncome)
        {
            var activity = Activity.Current;
            activity?.SetTag("business.application_id", applicationId);
            activity?.SetTag("business.document_type", docType.ToString());
            activity?.SetTag("business.s3_bucket", s3BucketName);
            activity?.SetTag("business.document_path", path);
            activity?.SetTag("business.query", query);
            if (docType == DocumentType.INCOMESTATEMENT)
                activity?.SetTag("business.min_income", minIncome.ToString());

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
            response = _textractClient.AnalyzeDocumentAsync(new AnalyzeDocumentRequest()
            {
                Document = new Document(){
                    S3Object = new S3Object(){ Bucket = s3BucketName, Name = path }
                },
                FeatureTypes = new List<string>() { "QUERIES" },
                QueriesConfig = queriesConfig
            }).Result;

            // Extract the query results from the Textract response into a variable called answers
            // I need only the response blocks containing query results.
            IEnumerable<Block> answers = response.Blocks.Where(b => b.BlockType == BlockType.QUERY_RESULT);

            // From hereon, we will evaluate business rule
            // Remember that when exiting, DocumentProcessingResult needs to have ApplicationId and Path
            // ensure that there is exactly 1 answer
            // else exit with status = 2, remarks = "No answer or multiple answers found
            if (answers.Count() != 1)
            {
                activity?.SetStatus(ActivityStatusCode.Error, "No answer or multiple answers found");
                return new DocumentProcessingResult
                {
                    ApplicationId = applicationId,
                    Path = path,
                    DocType = (int)docType,
                    Status = 2,
                    Remarks = "No answer or multiple answers found"
                };
            }

            // evaluate against business rule depending on the document type
            // if docType is DocumentType.INCOMESTATEMENT, try to parse the extracted income string as decimal based on the answer
            // If it can be parsed, exit with Status of either 10 or 1
            //    If the extracted income >= minIncome, then Status will be 10, remarks say "income meets income criteria"
            //    Else Status will be 1 and remarks say "income does not meet income criteria"
            // If the answer can't be parsed as decimal, exit with status = 2 and remarks saying income is not a proper numeric format
            //    Log a warning indicating that query is ok but extracted text is not a valid decimal
            if (docType == DocumentType.INCOMESTATEMENT)
            {
                string extractedIncomeString = answers.First().Text;
                if (decimal.TryParse(extractedIncomeString, out decimal extractedIncome))
                {
                    var status = extractedIncome >= minIncome ? 10 : 1;
                    activity?.SetStatus(ActivityStatusCode.Ok);
                    return new DocumentProcessingResult
                    {
                        ApplicationId = applicationId,
                        Path = path,
                        DocType = (int)docType,
                        Status = status,
                        Remarks = $"Income {extractedIncome} " + (extractedIncome >= minIncome ? "meets" : "does not meet") + " income criteria"
                    };
                }
                else
                {
                    _logger.LogWarning($"Query {query} matches {extractedIncomeString} but is not a valid decimal");
                    activity?.SetStatus(ActivityStatusCode.Error, "Income parsing failed");
                    return new DocumentProcessingResult
                    {
                        ApplicationId = applicationId,
                        Path = path,
                        DocType = (int)docType,
                        Status = 2, // pass for human review
                        Remarks = $"Income is not proper numeric format: {extractedIncomeString}"
                    };
                }
            }

            // if docType is DocumentType.IDENTITYDOCUMENT, try to parse the extracted expiry date string as DateTime based on the answer
            // If it can be parsed, exit with Status of either 10 or 1
            // If the extracted date is in the future, Status is 10, otherwise status is 1
            //    State expiry ddate in yyyy-MM-dd format in the remarks
            // If the answer can't be parsed as DateTime, exit with status = 2 and remarks saying income is not a proper date format
            //    Log a warning indicating that query is ok but extracted text is not a valid yyyy-MM-dd datetime format
            if (docType == DocumentType.IDENTITYDOCUMENT)
            {
                string extractedExpiryDateString = answers.First().Text;
                if (DateTime.TryParse(extractedExpiryDateString, out DateTime extractedExpiryDate))
                {
                    bool isStillValid = (extractedExpiryDate.CompareTo(DateTime.UtcNow) > 0);
                    activity?.SetTag("business.expiry_date", extractedExpiryDate.ToString("yyyy-MM-dd"));
                    activity?.SetTag("business.is_valid", isStillValid.ToString());
                    activity?.SetStatus(ActivityStatusCode.Ok);
                    return new DocumentProcessingResult
                    {
                        ApplicationId = applicationId,
                        Path = path,
                        DocType = (int)docType,
                        Status = isStillValid ? 10 : 1,
                        Remarks = "Document expiry date: " + extractedExpiryDate.ToString("yyyy-MM-dd")
                    };
                }
                else
                {
                    _logger.LogWarning($"Query {query} matches {extractedExpiryDateString} but is not a known valid date format");
                    activity?.SetStatus(ActivityStatusCode.Error, "Date parsing failed");
                    return new DocumentProcessingResult
                    {
                        ApplicationId = applicationId,
                        Path = path,
                        DocType = (int)docType,
                        Status = 2, // pass for human review
                        Remarks = $"Income is not recognized date format: {extractedExpiryDateString}"
                    };
                }
            }

            //if it ever come to here, throw exception indicating this is unknown state
            activity?.SetStatus(ActivityStatusCode.Error, "Unknown state");
            throw new Exception("Unknown state");
        }

        /// <summary>
        /// Publishes an event to EventBridge with the given payload.
        /// For eventName, use the eventNameOverride if it is not null or empty
        ///   Otherwise try to get from EventName, defaulting to application.document.processed f not found
        /// Publish to event bus specified in EventbusName environment variable
        /// Use the source as specified in ServiceName environment variable
        /// Logs the published event details.
        /// </summary>
        public void PublishEvent(object payload, string? eventNameOverride = "")
        {
            var eventName = (Environment.GetEnvironmentVariable("EventName") ?? "application.document.processed");
            eventName = string.IsNullOrEmpty(eventNameOverride) ? eventName : eventNameOverride;
            var activity = Activity.Current;
            activity?.SetTag("business.event_name", eventNameOverride ?? "application.document.processed");
            try
            {
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
                activity?.SetStatus(ActivityStatusCode.Ok, "Event published successfully");
                _logger.LogLine($"Event {eventName} published: {eventDetail}");
            }
            catch (Exception ex)
            {
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                _logger.LogLine($"Event {eventName} failed publishing: {ex.Message}");
                throw;
            }
        }

        public void Dispose()
        {
            _textractClient?.Dispose();
            _eventBridgeClient?.Dispose();
        }
    }
}