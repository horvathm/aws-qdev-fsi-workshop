using Amazon.Runtime;
using Microsoft.AspNetCore.Components.Forms;
using System.Net.Http.Headers;
using System.Security.Cryptography;


namespace ApplySample.Util
{
    internal static class HttpHelpers
    {
        public const string AWS_APIGATEWAY = "execute-api";

        public static HttpResponseMessage Get(string service, string region, string url)
        {
            var uri = new Uri(url);
            var now = DateTime.UtcNow;
            var amzDate = AWSSigner.ToAmzDate(now);
            var credential = FallbackCredentialsFactory.GetCredentials().GetCredentials();

            var authorizationHeader = AWSSigner.GetAuthorizationHeader(credential, service, region, "GET", uri, now);

            

            // Make the request
            using var client = new HttpClient();
            
            client.DefaultRequestHeaders.Add("Host", uri.Host);
            client.DefaultRequestHeaders.Add("x-amz-date", amzDate);
            if (!string.IsNullOrWhiteSpace(credential.Token))
            {
                client.DefaultRequestHeaders.Add("x-amz-security-token", credential.Token);
            }
            var canAdd = client.DefaultRequestHeaders.TryAddWithoutValidation("authorization", authorizationHeader);

            // check the header
            var authHeader = client.DefaultRequestHeaders.FirstOrDefault(h => h.Key == "authorization");

            return client.GetAsync(uri).Result;

        }

        public static HttpResponseMessage Post(string service, string region, string url, string payload)
        {
            var uri = new Uri(url);
            var now = DateTime.UtcNow;
            var amzDate = AWSSigner.ToAmzDate(now);
            var credential = FallbackCredentialsFactory.GetCredentials().GetCredentials();
            var payloadHash = AWSSigner.CalculateHash(payload);
            var headers = new Dictionary<string, string>
                {
                    {"x-amz-content-sha256", payloadHash},
                    {"content-length", payload.Length.ToString()},
                    {"content-type", "application/json"}
                };
            var authorizationHeader = AWSSigner.GetAuthorizationHeader(credential, service, region, "POST", uri, now, headers, payloadHash);

            // Make the request
            using var client = new HttpClient();
            client.DefaultRequestHeaders.Add("Host", uri.Host);
            client.DefaultRequestHeaders.Add("x-amz-date", amzDate);
            if (!string.IsNullOrWhiteSpace(credential.Token))
            {
                client.DefaultRequestHeaders.Add("x-amz-security-token", credential.Token);
            }
            client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", authorizationHeader);

            var requestContent = new StringContent(payload);
            requestContent.Headers.Remove("content-type");
            requestContent.Headers.Add("content-type", "application/json");
            requestContent.Headers.Add("content-length", payload.Length.ToString());
            requestContent.Headers.Add("x-amz-content-sha256", payloadHash);

            return client.PostAsync(uri, requestContent).Result;

        }


        public static async Task<HttpResponseMessage> PostFile(string service, string region, string url, IBrowserFile file, Dictionary<string, string> formFields = null)
        {
            var uri = new Uri(url);
            var now = DateTime.UtcNow;
            var amzDate = AWSSigner.ToAmzDate(now);
            var credential = FallbackCredentialsFactory.GetCredentials().GetCredentials();

            // Create multipart form content
            var multipartContent = new MultipartFormDataContent();

            // Read the file into memory stream
            using var fileStream = new MemoryStream();
            await file.OpenReadStream(maxAllowedSize: file.Size).CopyToAsync(fileStream);
            fileStream.Position = 0;

            // Add file content
            var fileContent = new StreamContent(fileStream);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue(file.ContentType);
            multipartContent.Add(fileContent, "file", file.Name);
            

            // Add additional form fields if any
            if (formFields != null)
            {
                foreach (var field in formFields)
                {
                    multipartContent.Add(new StringContent(field.Value), field.Key);
                }
            }

            // Get the complete multipart/form-data body as bytes, then compute hash
            string payloadHash;
            byte[] bodyBytes = await multipartContent.ReadAsByteArrayAsync(); // your complete multipart form data
            using (var sha256 = SHA256.Create())
            {
                byte[] hashBytes = sha256.ComputeHash(bodyBytes);
                payloadHash = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
            }

            // Get the boundary from the MultipartFormDataContent
            var boundary = multipartContent.Headers.ContentType.Parameters
                .First(p => p.Name.Equals("boundary", StringComparison.OrdinalIgnoreCase))
                .Value;
            
            // Prepare headers for signing
            var headers = new Dictionary<string, string>
                {
                    {"x-amz-content-sha256", payloadHash},
                    {"content-type", $"multipart/form-data; boundary={boundary}"}
                };

            var authorizationHeader = AWSSigner.GetAuthorizationHeader(credential, service, region, "POST", uri, now, headers, payloadHash);

            // Make the request
            using var client = new HttpClient();
            client.DefaultRequestHeaders.Add("Host", uri.Host);
            client.DefaultRequestHeaders.Add("x-amz-date", amzDate);
            if (!string.IsNullOrWhiteSpace(credential.Token))
            {
                client.DefaultRequestHeaders.Add("x-amz-security-token", credential.Token);
            }
            client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", authorizationHeader);

            multipartContent.Headers.Remove("content-type");
            multipartContent.Headers.Add("content-type", $"multipart/form-data; boundary={boundary}");
            multipartContent.Headers.Add("x-amz-content-sha256", payloadHash);


            return await client.PostAsync(uri, multipartContent);
        }

    }

}
