using ApplySample.Models;
using Microsoft.AspNetCore.Components;
using System.ComponentModel.DataAnnotations;
using Amazon.Runtime;
using ApplySample.Util;
using System.Text.Json;
using Amazon;

namespace ApplySample.Components.Pages
{
    public partial class CustomerDetails
    {
        [Inject]
        private IConfiguration _config { get; set; }

        [Inject]
        private AWSConfig _awsconfig { get; set; }

        private CustomerDetailModel model = new();

        private string errorMessage;

        [CascadingParameter]
        public Home Parent { get; set; }

        private async Task HandleValidSubmit()
        {

            try
            {
                var regionString = _config["AWS:Region"];
                //var apiBaseUrl = _config["AWS:ApiGateway:InvokeUrl"];
                var apiBaseUrl = _awsconfig.GetStringFromSSM(_config["AWS:ApiGateway:InvokeUrl"]);
                //var resourcePath = _config["AWS:ApiGateway:CustomerPath"];
                var resourcePath = _awsconfig.GetStringFromSSM(_config["AWS:ApiGateway:CustomerPath"]);
                var apiPath = resourcePath.Replace("{applicationId}", Parent.ApplicationId);
                //remove trailing / in apiBaseUrl
                if (apiBaseUrl.EndsWith("/"))
                {
                    apiBaseUrl = apiBaseUrl.Substring(0, apiBaseUrl.Length - 1);
                }
                var apiInvokeUrl = $"{apiBaseUrl}{apiPath}";

                var httpClient = new HttpClient();
                var response = HttpHelpers.Post(HttpHelpers.AWS_APIGATEWAY, regionString, apiInvokeUrl, JsonSerializer.Serialize(model));


                if (response.IsSuccessStatusCode)
                {
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

            Console.WriteLine("Form submitted successfully");

            // Store the form data or process it as needed

            // Navigate to next step
            Parent.GoToStep(ApplicationSteps.Income);

        }

        public class CustomerDetailModel
        {
            public string FirstName { get; private set; } = "Dummy";

            public string LastName { get; private set; } = "Dummy";

            public string Email { get; private set; } = "Dummy";

            [Required(ErrorMessage = "Phone number is required")]
            [RegularExpression(@"^[+]?[\s]?[(]?[0-9]{1,4}[)]?[-\s\.]?[0-9]{1,4}[-\s\.]?[0-9]{1,9}$", ErrorMessage = "Please enter valid phone number")]
            public string PhoneNumber { get; set; }

            public string Address { get; set; }


            public string City { get; set; }

            public string State { get; set; }

            [Required(ErrorMessage = "Postcode is required")]
            [RegularExpression(@"^\d{4,}$", ErrorMessage = "Postcode must be at least 4 digits")]
            public string Postcode { get; set; }

            [Required(ErrorMessage = "Country is required")]
            public string Country { get; set; }
        }
    }
}
