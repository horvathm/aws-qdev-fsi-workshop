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
using System.Diagnostics;

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
        private readonly ActivitySource _tracer;

        public ApplicationService(ILogger<ApplicationService> logger, IConfiguration configuration, IAWSConfig awsConfig, IInstrumentation instrumentation)
        {
            _logger = logger;
            _configuration = configuration;
            _awsregion = Amazon.RegionEndpoint.GetBySystemName(_configuration["AWS:Region"]);
            _dbContext = new DynamoDBContext(new AmazonDynamoDBClient());
            _s3Client = new AmazonS3Client(_awsregion);
            _eventBridgeClient = new AmazonEventBridgeClient();
            _awsConfig = awsConfig;
            ArgumentNullException.ThrowIfNull(instrumentation);
            _tracer = instrumentation.ActivitySource;
        }


        public Application GetApplication(Guid applicationId)
        {
            using var activity = _tracer.StartActivity("GetApplication", ActivityKind.Server);
            activity?.SetTag("business.ApplicationId", applicationId);

            try
            {
                var application = _dbContext.LoadAsync<Application>(applicationId).GetAwaiter().GetResult();
                if (application == null)
                {
                    var errMessage = $"Application with ID {applicationId} not found";
                    _logger.LogWarning(errMessage);
                    activity?.SetStatus(ActivityStatusCode.Error, errMessage);
                    throw new InvalidOperationException(errMessage);
                }
                activity?.SetStatus(ActivityStatusCode.Ok);
                return application;
            }
            catch (ResourceNotFoundException ex)
            {
                var errMessage = $"Application with ID {applicationId} not found";
                _logger.LogWarning(errMessage);
                activity?.SetStatus(ActivityStatusCode.Error, errMessage);
                throw new InvalidOperationException(errMessage, ex);
            }
            catch (Exception ex)
            {
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                LogError(ex);
                throw;
            }
        }

        public void CreateApplication(Application application)
        {
            using var activity = _tracer.StartActivity("CreateApplication", ActivityKind.Server);
            activity?.SetTag("business.ApplicationId", application.ApplicationId);
            
            try
            {
                _dbContext.SaveAsync(application).GetAwaiter().GetResult();
                PublishEvent("application.started", new
                {
                    ApplicationId = application.ApplicationId,
                    UtcTime = DateTime.UtcNow.ToString("g")
                }, activity);
                
                activity?.SetStatus(ActivityStatusCode.Ok);
            }
            catch (ConditionalCheckFailedException ex)
            {
                var errMessage = $"Application with ID {application.ApplicationId} already exists";
                _logger.LogWarning(errMessage);
                activity?.SetStatus(ActivityStatusCode.Error, errMessage);
                throw new InvalidOperationException(errMessage, ex);
            }
            catch (Exception ex)
            {
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                LogError(ex);
                throw;
            }
        }

        public void UpdateApplication(Application application)
        {
            using var activity = _tracer.StartActivity("UpdateApplication", ActivityKind.Server);
            activity?.SetTag("business.ApplicationId", application.ApplicationId);
            
            try
            {
                // Verify the item exists first - GetApplication is already instrumented
                var existingApplication = GetApplication(application.ApplicationId);
                
                // Save the updated application
                _dbContext.SaveAsync(application).GetAwaiter().GetResult();
                PublishEvent("application.data.add", new
                {
                    ApplicationId = application.ApplicationId,
                    UtcTime = DateTime.UtcNow.ToString("g")
                }, activity);
                
                activity?.SetStatus(ActivityStatusCode.Ok);
            }
            catch (ConditionalCheckFailedException ex)
            {
                var errMessage = $"Unable to update Application with ID {application.ApplicationId}. The item may have been modified by another process.";
                _logger.LogWarning(errMessage);
                activity?.SetStatus(ActivityStatusCode.Error, errMessage);
                throw new InvalidOperationException(errMessage, ex);
            }
            catch (Exception ex)
            {
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                LogError(ex);
                throw;
            }
        }

        public void SubmitApplicationFile(Guid applicationId, DocumentType docuType, string fileName, Stream file)
        {
            using var activity = _tracer.StartActivity("SubmitApplicationFile", ActivityKind.Server);
            activity?.SetTag("business.ApplicationId", applicationId);
            activity?.SetTag("business.DocumentType", docuType.ToString());
            activity?.SetTag("business.FileName", fileName);
            
            if (file == null || file.Length == 0)
            {
                var errMessage = $"File is empty. No uploads made against application {applicationId}.";
                _logger.LogError(errMessage);
                activity?.SetStatus(ActivityStatusCode.Error, errMessage);
                throw new InvalidOperationException(errMessage);
            }
            
            Application application;
            try
            {
                // GetApplication is already instrumented
                application = GetApplication(applicationId);
            }
            catch (Exception)
            {
                // The activity status will be set by GetApplication
                throw;
            }

            try
            {
                var s3UploadFolder = _awsConfig.GetStringFromSSM(_configuration["AWS:S3:UploadFolder"]);
                var s3Key = $"{s3UploadFolder}/{applicationId}/{docuType}/{fileName}";
                var bucketName = _awsConfig.GetStringFromSSM(_configuration["AWS:S3:BucketName"]);
                
                activity?.SetTag("business.S3Key", s3Key);
                activity?.SetTag("business.BucketName", bucketName);
                
                using (var uploadActivity = _tracer.StartActivity("S3Upload", ActivityKind.Client))
                using (TransferUtility utility = new TransferUtility(_s3Client))
                {
                    uploadActivity?.SetTag("business.S3Key", s3Key);
                    uploadActivity?.SetTag("business.BucketName", bucketName);
                    
                    TransferUtilityUploadRequest request = new TransferUtilityUploadRequest();
                    request.BucketName = bucketName;
                    request.Key = s3Key;
                    request.InputStream = file;
                    utility.Upload(request);
                    
                    uploadActivity?.SetStatus(ActivityStatusCode.Ok);
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
                }, activity);
                
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
                    }, activity);
                }
                
                activity?.SetStatus(ActivityStatusCode.Ok);
            }
            catch (Exception ex)
            {
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                LogError(ex);
                throw;
            }
        }

        private void PublishEvent(string eventName, object payload, Activity parentActivity = null)
        {
            using var activity = _tracer.StartActivity("PublishEvent", ActivityKind.Producer, parentActivity?.Id ?? default);
            activity?.SetTag("business.EventName", eventName);
            
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
                activity?.SetStatus(ActivityStatusCode.Ok);
            }
            catch (Exception ex)
            {
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                LogError(ex);
                throw;
            }
        }

        private void LogError(Exception ex, [CallerMemberName] string methodName = "")
        {
            _logger.LogError(ex, "Error in {MethodName}", methodName);
            
            // Capture the exception in the current activity if one exists
            Activity.Current?.SetStatus(ActivityStatusCode.Error, ex.Message);
        }

    }
}