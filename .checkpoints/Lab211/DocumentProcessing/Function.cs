using Amazon.Lambda.Core;
using Amazon.Lambda.Serialization.SystemTextJson;
using System.Text.Json;
using Amazon.Lambda.CloudWatchEvents;
using DocumentProcessing.Model;
using DocumentProcessing.Services;

[assembly: LambdaSerializer(typeof(DefaultLambdaJsonSerializer))]

namespace DocumentProcessing
{
    public class Function
    {
        public void Handler(CloudWatchEvent<ApplicationEvent> applicationEvent, ILambdaContext context)
        {
            try
            {
                context.Logger.LogLine($"Lambda received event ApplicationId: {applicationEvent.Detail.ApplicationId}, DocType: {applicationEvent.Detail.DocType}, Path: {applicationEvent.Detail.Path}");

                string s3BucketName = Environment.GetEnvironmentVariable("S3BucketName");
                decimal minIncome = decimal.Parse(Environment.GetEnvironmentVariable("MinIncome"));

                string query;
                if (applicationEvent.Detail.DocType == DocumentType.INCOMESTATEMENT) query = Environment.GetEnvironmentVariable("IncomeQuery");
                else if (applicationEvent.Detail.DocType == DocumentType.IDENTITYDOCUMENT) query = Environment.GetEnvironmentVariable("IdDocQuery");
                else
                {
                    context.Logger.LogWarning($"There is no extraction implementation concerning DocType: {applicationEvent.Detail.DocType}");
                    return;
                }

                using var _documentProcessor = new DocumentProcessor(context.Logger);
                var _result = _documentProcessor.ProcessDocument(applicationEvent.Detail.ApplicationId, applicationEvent.Detail.DocType, s3BucketName, applicationEvent.Detail.Path, query, minIncome);
                _documentProcessor.PublishEvent(_result);
            }
            catch (Exception ex)
            {
                context.Logger.LogError("Error in FunctionHandler: " + ex.Message);
                throw;
            }
        }
    }
}