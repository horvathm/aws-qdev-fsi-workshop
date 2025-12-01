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
                
            }
            catch (Exception ex)
            {
                context.Logger.LogError("Error in FunctionHandler: " + ex.Message);
                throw;
            }
        }

    }
}