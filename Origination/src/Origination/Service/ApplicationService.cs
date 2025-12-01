using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.DataModel;
using Amazon.DynamoDBv2.Model;
using Amazon.EventBridge;
using Amazon.S3;
using Amazon.S3.Transfer;
using Origination.Model;
using Origination.Helpers;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace Origination.Service
{
    public class ApplicationService : IApplicationService
    {
        private readonly IDynamoDBContext _dbContext;
        private readonly AmazonS3Client _s3Client;
        private readonly AmazonEventBridgeClient _eventBridgeClient;
        private readonly ILogger<ApplicationService> _logger;
        private readonly IConfiguration _configuration;
        private readonly IAWSConfig _awsConfig;
        private readonly Amazon.RegionEndpoint _awsregion;
        
        public ApplicationService(ILogger<ApplicationService> logger, IConfiguration configuration, IAWSConfig awsconfig)
        {
            _logger = logger;
            _configuration = configuration;
            _awsregion = Amazon.RegionEndpoint.GetBySystemName(_configuration["AWS:Region"]);
            _dbContext = new DynamoDBContext(new AmazonDynamoDBClient());
            _s3Client = new AmazonS3Client(_awsregion);
            _eventBridgeClient = new AmazonEventBridgeClient();
            _awsConfig = awsconfig;
        }

        public Application GetApplication(Guid applicationId)
        {
            try
            {
                throw new NotImplementedException();
            }
            catch (ResourceNotFoundException)
            {
                _logger.LogWarning($"Application with ID {applicationId} not found");
                throw new InvalidOperationException($"Application with ID {applicationId} not found");
            }
            catch (Exception ex)
            {
                LogError(ex);
                throw;
            }
        }

        public void CreateApplication(Application application)
        {
            try
            {
                throw new NotImplementedException();
            }
            catch (ConditionalCheckFailedException)
            {
                _logger.LogWarning($"Application with ID {application.ApplicationId} already exists");
                throw new InvalidOperationException($"Application with ID {application.ApplicationId} already exists");
            }
            catch (Exception ex)
            {
                LogError(ex);
                throw;
            }
        }

        public void UpdateApplication(Application application)
        {
            try
            {
                throw new NotImplementedException();
            }
            catch (ConditionalCheckFailedException)
            {
                _logger.LogWarning($"Unable to update Application with ID {application.ApplicationId}. The item may have been modified by another process.");
                throw new InvalidOperationException($"Unable to update Application with ID {application.ApplicationId}. The item may have been modified by another process.");
            }
            catch (Exception ex)
            {
                LogError(ex);
                throw;
            }
        }

        public void SubmitApplicationFile(Guid applicationId, DocumentType docuType, string fileName, Stream file)
        {
            if (file == null || file.Length == 0)
            {
                _logger.LogError($"File is empty. No uploads made against application {applicationId}.");
                throw new InvalidOperationException($"File is empty. No uploads made against application {applicationId}.");
            }
            Application application;
            try
            {
                application = GetApplication(applicationId);
            }
            catch (Exception)
            {
                throw;
            }

            try
            {
                throw new NotImplementedException();
            }
            catch (Exception ex)
            {
                LogError(ex);
                throw;
            }
        }

        private void PublishEvent(string eventName, object payload)
        {
            try
            {
                _eventBridgeClient.PutEventsAsync(new Amazon.EventBridge.Model.PutEventsRequest()
                {
                    Entries = new List<Amazon.EventBridge.Model.PutEventsRequestEntry>()
                                {
                                    new Amazon.EventBridge.Model.PutEventsRequestEntry()
                                    {
                                        EventBusName = _awsconfig.GetStringFromSSM(_configuration["AWS:EventBridge:EventbusName"]),
                                        Source = _configuration["ServiceName"],
                                        Detail = JsonSerializer.Serialize(payload),
                                        DetailType = eventName,
                                        Time = DateTime.UtcNow
                                    }
                                }
                }).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                LogError(ex);
                throw;
            }
        }

        private void LogError(Exception ex, [CallerMemberName] string methodName = "")
        {
            _logger.LogError(ex, "Error in {MethodName}", methodName);
        }

    }

}