using ApplySample.Models;
using Microsoft.AspNetCore.Components;
using System.ComponentModel.DataAnnotations;
using Amazon.Runtime;
using ApplySample.Util;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ApplySample.Components.Pages
{
    public partial class ApplyStart
    {
        [Inject]
        private IConfiguration _config { get; set; }

        [Inject]
        private AWSConfig _awsconfig { get; set; }


        private ApplicationModel model;
        private bool isLoading;
        private string errorMessage;


        [CascadingParameter]
        public Home Parent { get; set; }

        protected override void OnInitialized()
        {
            // Initialize model with default ProductType from configuration
            model = new ApplicationModel
            {
                ProductType = _config["App:ProductCode"]
            };
        }

        private async Task HandleValidSubmit()
        {
            try
            {
                isLoading = true;
                errorMessage = null;

                var regionString = _config["AWS:Region"];
                var apiBaseUrl = _awsconfig.GetStringFromSSM(_config["AWS:ApiGateway:InvokeUrl"]);
                var apipath = _awsconfig.GetStringFromSSM(_config["AWS:ApiGateway:ApplicationPath"]);
                //var apiBaseUrl = _config["AWS:ApiGateway:InvokeUrl"];
                //var apipath = _config["AWS:ApiGateway:ApplicationPath"];
                //remove trailing / in apiBaseUrl
                if (apiBaseUrl.EndsWith("/"))
                {
                    apiBaseUrl = apiBaseUrl.Substring(0, apiBaseUrl.Length - 1);
                }
                var apiInvokeUrl = $"{apiBaseUrl}{apipath}";



                var httpClient = new HttpClient();
                var response = HttpHelpers.Post(HttpHelpers.AWS_APIGATEWAY, regionString, apiInvokeUrl, JsonSerializer.Serialize(model));


                if (response.IsSuccessStatusCode)
                {
                    var responseMessage = await response.Content.ReadAsStringAsync();
                    var guidMatch = Regex.Match(responseMessage, @"[{]?[0-9a-fA-F]{8}-([0-9a-fA-F]{4}-){3}[0-9a-fA-F]{12}[}]?");
                    if (guidMatch.Success)
                    {
                        Parent.ApplicationId = guidMatch.Value.Trim('{', '}');
                    }
                    else throw new Exception($"Failure extracting Guid from {responseMessage}, subsequent operations will fail");
                    Parent.GoToStep(ApplicationSteps.Details);
                }
                else
                {
                    errorMessage = $"Failed to submit application. Status: {response.StatusCode}";
                    var content = await response.Content.ReadAsStringAsync();
                    errorMessage += $"<br/>{content}";
                    Console.WriteLine($"Error content: {content}");
                }
            }
            catch (Exception ex)
            {
                errorMessage = $"An error occurred while submitting your application. {ex.Message}";
                Console.WriteLine($"Error calling API: {ex.Message}");
            }
            

        }

        public class ApplicationModel
        {
            public string ProductType { get; set; }

            [Required(ErrorMessage = "First name is required")]
            [Display(Name = "First Name")]
            public string FirstName { get; set; }

            [Required(ErrorMessage = "Last name is required")]
            [Display(Name = "Last Name")]
            public string LastName { get; set; }

            [Required(ErrorMessage = "Email is required")]
            [EmailAddress(ErrorMessage = "Please enter a valid email address")]
            public string Email { get; set; }
        }
    }

    
}
