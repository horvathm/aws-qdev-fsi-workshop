using DocumentProcessing.Model;
using Amazon.Lambda.Core;
using Amazon.Textract.Model;
using Amazon.Textract;
using Amazon.EventBridge.Model;
using Amazon.EventBridge;
using System.Text.Json;

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
            response = 

            // Extract the query results from the Textract response into a variable called answers
            // I need only the response blocks containing query results.
            IEnumerable<Block> answers = 

            // ensure that there is exactly 1 answer
            // else we will publish a failure event indicating no answer or multiple answers found and exit

            // evaluate against business rule depending on the document type
            // if docType is DocumentType.INCOMESTATEMENT, try to parse the extracted income string as decimal based on the answer
            // If it can be parsed, publish an application.document.processed event
            // If the extracted income >= minIncome Status of event is 10, remarks say income meets criteria
            //    Otherwise Status of event is 1 and remarks say income does not meet criteria
            // If the answer can't be parsed as decimal, publish a failure event and remarks saying income is not a proper numeric format

            // if docType is DocumentType.IDENTITYDOCUMENT, try to parse the extracted expiry date string as DateTime based on the answer
            // If it can be parsed, publish an application.document.processed event
            // If the extracted date is in the future, Status of event is 10, otherwise status is 1
            // State expiry ddate in yyyy-MM-dd format in the remarks
            // If the answer can't be parsed as DateTime, publish a failure event and remarks saying income is not a proper date format

            //if it ever come to here, throw exception indicating this is unknown state
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
            
        }

        public void Dispose()
        {
            _textractClient?.Dispose();
            _eventBridgeClient?.Dispose();
        }
    }
}