using ApplySample.Models;
using Microsoft.AspNetCore.Components;
using System.ComponentModel.DataAnnotations;
using Amazon.Runtime;
using ApplySample.Util;
using Microsoft.AspNetCore.Components.Forms;

namespace ApplySample.Components.Pages
{
    public partial class ProofIncome
    {
        [Inject]
        private IConfiguration _config { get; set; }

        [Inject]
        private AWSConfig _awsconfig { get; set; }

        [CascadingParameter]
        public Home Parent { get; set; }

        private string uploadMessage;
        private string messageClass;
        private string errorMessage;
        private IBrowserFile uploadedFile;
        private long maxFileSize;
        private FileModel model = new();

        protected override void OnInitialized()
        {
            maxFileSize = long.Parse(_config["App:MaxFileMB"]) * 1024 * 1024;
        }

        private async Task HandleFileUpload(InputFileChangeEventArgs e)
        {
            try
            {
                uploadedFile = e.File;
                var fileType = Path.GetExtension(uploadedFile.Name).ToLower();

                // Validate file type
                if (fileType != ".pdf" && fileType != ".jpg" && fileType != ".jpeg" && fileType != ".png")
                {
                    uploadMessage = "Please select a PDF, JPG, JPEG, or PNG file.";
                    messageClass = "text-danger";
                    return;
                }

                // Validate file size
                if (uploadedFile.Size > maxFileSize)
                {
                    uploadMessage = $"File size must be less than {maxFileSize}MB.";
                    messageClass = "text-danger";
                    return;
                }

                var regionString = _config["AWS:Region"];
                //var apiBaseUrl = _config["AWS:ApiGateway:InvokeUrl"];
                var apiBaseUrl = _awsconfig.GetStringFromSSM(_config["AWS:ApiGateway:InvokeUrl"]);
                //var resourcePath = _config["AWS:ApiGateway:FilePath"];
                var resourcePath = _awsconfig.GetStringFromSSM(_config["AWS:ApiGateway:FilePath"]);
                var apiPath = resourcePath.Replace("{applicationId}", Parent.ApplicationId);
                //remove trailing / in apiBaseUrl
                if (apiBaseUrl.EndsWith("/"))
                {
                    apiBaseUrl = apiBaseUrl.Substring(0, apiBaseUrl.Length - 1);
                }
                var apiInvokeUrl = $"{apiBaseUrl}{apiPath}?docuType=1"; //1 = INCOMESTATEMENT, 2 = ID, 3 = SELFIE

                var httpClient = new HttpClient();
                var response = await HttpHelpers.PostFile(HttpHelpers.AWS_APIGATEWAY, regionString, apiInvokeUrl, e.File);

                if (response.IsSuccessStatusCode)
                {
                    // File is valid
                    uploadMessage = $"File {uploadedFile.Name} uploaded successfully.";
                    messageClass = "text-success";

                    Parent.GoToStep(ApplicationSteps.Identity);
                }
                else
                {
                    errorMessage = $"Failed to submit file. Status: {response.StatusCode}";
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

        private void HandleValidSubmit()
        {
            if (uploadedFile == null)
            {
                uploadMessage = "Please upload a valid file before proceeding.";
                messageClass = "text-danger";
                return;
            }

            Parent.GoToStep(ApplicationSteps.Confirm);
        }

        public class FileModel
        {
            [Required(ErrorMessage = "Please upload a valid file")]
            public bool HasFile { get; set; }
        }
    }
}
