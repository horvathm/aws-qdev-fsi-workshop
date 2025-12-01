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
                var application = _dbContext.LoadAsync<Application>(applicationId).GetAwaiter().GetResult();
                if (application == null)
                {
                    _logger.LogWarning($"Application with ID {applicationId} not found");
                    throw new InvalidOperationException($"Application with ID {applicationId} not found");
                }
                return application;
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
                _dbContext.SaveAsync(application).GetAwaiter().GetResult();
                PublishEvent("application.started", new
                {
                    ApplicationId = application.ApplicationId,
                    UtcTime = DateTime.UtcNow.ToString("g")
                });
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
                // Verify the item exists first
                var existingApplication = GetApplication(application.ApplicationId);
                if (existingApplication == null)
                {
                    _logger.LogWarning($"Application with ID {application.ApplicationId} not found, nothing is updated");
                    throw new KeyNotFoundException($"Application with ID {application.ApplicationId} not found");
                }

                // Save the updated application
                _dbContext.SaveAsync(application).GetAwaiter().GetResult();
                PublishEvent("application.data.add", new
                {
                    ApplicationId = application.ApplicationId,
                    UtcTime = DateTime.UtcNow.ToString("g")
                });
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
                var s3UploadFolder = _awsConfig.GetStringFromSSM(_configuration["AWS:S3:UploadFolder"]);
                var s3Key = $"{s3UploadFolder}/{applicationId}/{docuType}/{fileName}";
                var bucketName = _awsConfig.GetStringFromSSM(_configuration["AWS:S3:BucketName"]);
                using (TransferUtility utility = new TransferUtility(_s3Client))
                {
                    TransferUtilityUploadRequest request = new TransferUtilityUploadRequest();
                    request.BucketName = bucketName;
                    request.Key = s3Key;
                    request.InputStream = file;
                    utility.Upload(request);
                }

                switch (docuType)
                {
                    case DocumentType.INCOMESTATEMENT:
                        application.Status.IncomeRequirement.FileRef = s3Key;
                        break;
                    case DocumentType.IDENTITYDOCUMENT:
                        application.Status.IdDocValidity.FileRef = s3Key;
                        break;
                    case DocumentType.SELFIE:
                        application.Status.Ekyc.FileRef = s3Key;
                        break;
                }
                _dbContext.SaveAsync(application).GetAwaiter().GetResult();

                PublishEvent("application.file.upload", new
                {
                    ApplicationId = application.ApplicationId,
                    DocType = (int)docuType,
                    Path = s3Key,
                    UtcTime = DateTime.UtcNow.ToString("g")
                });
                if ((docuType == DocumentType.IDENTITYDOCUMENT || docuType == DocumentType.SELFIE)
                    && !(application.Status.Ekyc.Status == 10 && application.Status.IdDocValidity.Status == 10)
                    && !string.IsNullOrEmpty(application.Status.Ekyc.FileRef)
                    && !string.IsNullOrEmpty(application.Status.IdDocValidity.FileRef))
                {
                    PublishEvent("application.kyc.try", new
                    {
                        ApplicationId = application.ApplicationId,
                        Path1 = application.Status.IdDocValidity.FileRef,
                        Path2 = application.Status.Ekyc.FileRef,
                        UtcTime = DateTime.UtcNow.ToString("g")
                    });
                }
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
                                        EventBusName = _awsConfig.GetStringFromSSM(_configuration["AWS:EventBridge:EventbusName"]),
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