using Amazon.SimpleSystemsManagement;
using Amazon.SimpleSystemsManagement.Model;

namespace Origination.Helpers
{
    public class AWSConfig : IAWSConfig
    {
        private readonly AmazonSimpleSystemsManagementClient _ssmClient;
        private readonly ILogger<AWSConfig> _logger;
        private readonly IConfiguration _configuration;
        private readonly Amazon.RegionEndpoint _awsregion;

        public AWSConfig(ILogger<AWSConfig> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
            _awsregion = Amazon.RegionEndpoint.GetBySystemName(
                Environment.GetEnvironmentVariable("AWS_REGION") ?? 
                _configuration["AWS:Region"] ?? 
                throw new ArgumentNullException("AWS Region not found in environment variables or configuration"));
            _ssmClient = new AmazonSimpleSystemsManagementClient(_awsregion);
        }

        public string GetStringFromSSM(string parameterName)
        {
            string parameterValue = "";
            try
            {
                var response = _ssmClient.GetParameterAsync(new GetParameterRequest()
                {
                    Name = parameterName
                }).Result;
                parameterValue = response.Parameter.Value;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in GetStringFromSSM");
                throw ex;
            }
            return parameterValue;
        }

    }
}
