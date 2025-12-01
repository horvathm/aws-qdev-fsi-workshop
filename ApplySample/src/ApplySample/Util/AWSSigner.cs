using System.Security.Cryptography;
using System.Text;
using Amazon.Runtime;

namespace ApplySample.Util
{

    internal class AWSSigner
    {

        public static string GetAuthorizationHeader(ImmutableCredentials credentials, string service, string region,
                string httpMethod, Uri uri, DateTime now)
        {
            return GetAuthorizationHeader(credentials, service, region, httpMethod, uri, now, new Dictionary<string, string>(), CalculateHash(""));
        }


        public static string GetAuthorizationHeader(ImmutableCredentials credentials,
            string service, string region, string httpMethod,
            Uri uri, DateTime now, IDictionary<string, string> headers, string payloadHash)
        {
            var ALGORITHM = "AWS4-HMAC-SHA256";

            var amzDate = ToAmzDate(now);
            var datestamp = now.ToString("yyyyMMdd");
            headers.Add("host", uri.Host);
            headers.Add("x-amz-date", amzDate);
            if (!string.IsNullOrWhiteSpace(credentials.Token))
            {
                headers.Add("x-amz-security-token", credentials.Token);
            }

            // Create the canonical request        
            var canonicalQuerystring = uri.Query;
            // if canonicalQuerystring begins with '?', remove the '?'
            if (!string.IsNullOrEmpty(canonicalQuerystring) && canonicalQuerystring.StartsWith("?"))
            {
                canonicalQuerystring = canonicalQuerystring.Substring(1);
            }

            var canonicalHeaders = CanonicalizeHeaders(headers);
            var signedHeaders = CanonicalizeHeaderNames(headers);
            var canonicalRequest = $"{httpMethod}\n{uri.AbsolutePath}\n{canonicalQuerystring}\n{canonicalHeaders}\n{signedHeaders}\n{payloadHash}";
            //should be POST\n/prod/application/8899243a-ad63-4aeb-95ac-6c80e9d1015a/file\ndocuType=1\nhost:ywl7fjouy3.execute-api.ap-southeast-1.amazonaws.com\nx-amz-date:20241219T050936Z\n\nhost;x-amz-date\n4999f3b69129fd8dcb120627282dd1c90cbe4611c8310d46b06b740bce39ad41'\

            // Create the string to sign
            var credentialScope = $"{datestamp}/{region}/{service}/aws4_request";
            var hashedCanonicalRequest = CalculateHash(canonicalRequest);
            var stringToSign = $"{ALGORITHM}\n{amzDate}\n{credentialScope}\n{hashedCanonicalRequest}";
            //should be 'AWS4-HMAC-SHA256\n20241219T050936Z\n20241219/ap-southeast-1/execute-api/aws4_request\nafd087dfc3ad16a7f9d668df1dcbe7dfb9f772062cefb89cfbbfee0a11c8567b

            // Sign the string
            var signingKey = GetSignatureKey(credentials.SecretKey, datestamp, region, service);
            var signature = CalculateHmacHex(signingKey, stringToSign);

            // return signing information
            return $"{ALGORITHM} Credential={credentials.AccessKey}/{credentialScope}, SignedHeaders={signedHeaders}, Signature={signature}";
        }

        public static string ToAmzDate(DateTime date)
        {
            return date.ToString("yyyyMMddTHHmmssZ");
        }

        static string CanonicalizeHeaderNames(IDictionary<string, string> headers)
        {
            var headersToSign = new List<string>(headers.Keys);
            headersToSign.Sort(StringComparer.OrdinalIgnoreCase);

            var sb = new StringBuilder();
            foreach (var header in headersToSign)
            {
                if (sb.Length > 0)
                    sb.Append(";");
                sb.Append(header.ToLower());
            }
            return sb.ToString();
        }

        static string CanonicalizeHeaders(IDictionary<string, string> headers)
        {
            var canonicalHeaders = new StringBuilder();
            var sortedHeaders = new SortedDictionary<string, string>(headers, StringComparer.OrdinalIgnoreCase);
            foreach (var header in sortedHeaders)
            {
                canonicalHeaders.Append($"{header.Key.ToLowerInvariant()}:{header.Value.Trim()}\n");
            }
            return canonicalHeaders.ToString();
        }

        static byte[] GetSignatureKey(string key, string dateStamp, string regionName, string serviceName)
        {
            var kSecret = Encoding.UTF8.GetBytes($"AWS4{key}");
            var kDate = HmacSha256(kSecret, dateStamp);
            var kRegion = HmacSha256(kDate, regionName);
            var kService = HmacSha256(kRegion, serviceName);
            return HmacSha256(kService, "aws4_request");
        }

        static string CalculateHmacHex(byte[] key, string data)
        {
            var hash = HmacSha256(key, data);
            return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }

        static byte[] HmacSha256(byte[] key, string data)
        {
            using HMACSHA256 hmac = new HMACSHA256(key);
            return hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
        }

        public static string CalculateHash(string data)
        {
            using SHA256 sha256 = SHA256.Create();
            var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(data));
            return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }

        public static string ToHexString(byte[] data, bool lowercase)
        {
            var sb = new StringBuilder();
            for (var i = 0; i < data.Length; i++)
            {
                sb.Append(data[i].ToString(lowercase ? "x2" : "X2"));
            }
            return sb.ToString();
        }
    }
}
