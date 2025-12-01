using Amazon.SimpleSystemsManagement;
using Amazon.SimpleSystemsManagement.Model;

namespace ApplySample.Util
{
    public class AWSConfig
    {
        private readonly AmazonSimpleSystemsManagementClient _ssmClient;
        private readonly ILogger<AWSConfig> _logger;
        private readonly IConfiguration _configuration;
        private readonly Amazon.RegionEndpoint _awsregion;

        public AWSConfig(ILogger<AWSConfig> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
            _awsregion = Amazon.RegionEndpoint.GetBySystemName(_configuration["AWS:Region"]);
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
