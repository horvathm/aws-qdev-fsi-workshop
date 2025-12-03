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
                context.Logger.LogInformation($"Lambda received event ApplicationId: {applicationEvent.Detail.ApplicationId}, DocType: {applicationEvent.Detail.DocType}, Path: {applicationEvent.Detail.Path}");

                var s3BucketName = Environment.GetEnvironmentVariable("S3BucketName");
                var minIncomeStr = Environment.GetEnvironmentVariable("MinIncome");
                var minIncome = decimal.Parse(minIncomeStr);

                string query;
                if (applicationEvent.Detail.DocType == DocumentType.INCOMESTATEMENT)
                {
                    query = Environment.GetEnvironmentVariable("IncomeQuery");
                }
                else if (applicationEvent.Detail.DocType == DocumentType.IDENTITYDOCUMENT)
                {
                    query = Environment.GetEnvironmentVariable("IdDocQuery");
                }
                else
                {
                    context.Logger.LogWarning($"Unknown DocType: {applicationEvent.Detail.DocType}");
                    return;
                }

                using var processor = new DocumentProcessor(context.Logger);
                var _result = processor.ProcessDocument(
                    applicationEvent.Detail.ApplicationId,
                    applicationEvent.Detail.DocType,
                    s3BucketName,
                    applicationEvent.Detail.Path,
                    query,
                    minIncome);

                processor.PublishEvent(_result);
            }
            catch (Exception ex)
            {
                context.Logger.LogError("Error in FunctionHandler: " + ex.Message);
                throw;
            }
        }

    }
}