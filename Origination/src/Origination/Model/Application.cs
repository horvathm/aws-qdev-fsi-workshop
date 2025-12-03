using Amazon.DynamoDBv2.DataModel;

namespace Origination.Model
{
    public class GranularStatus
    {
        public int Status { get; set; } = 0; // 0 = NotStarted, 10 = Pass, 1 = Fail, 2 = Escalated
        public string FileRef { get; set; }
        public string Remarks { get; set; }
    }

    public class ApplicationStatus
    {
        public GranularStatus Ekyc { get; set; } = new GranularStatus();
        public GranularStatus IdDocValidity { get; set; } = new GranularStatus();
        public GranularStatus IncomeRequirement { get; set; } = new GranularStatus();

        public int OverallStatus
        {
            get
            {
                var statuses = new[] { Ekyc.Status, IdDocValidity.Status, IncomeRequirement.Status };

                // If all are NotStarted (0), return NotStarted
                if (statuses.All(s => s == 0))
                    return 0;

                // If any is Fail (1), return Fail
                if (statuses.Any(s => s == 1))
                    return 1;

                // If all are Pass (10), return Pass
                if (statuses.All(s => s == 10))
                    return 10;

                // Otherwise return Escalated (2)
                return 2;
            }
        }
    }

    [DynamoDBTable("Application")]
    public class Application
    {
        [DynamoDBHashKey]
        public Guid ApplicationId { get; set; }

        [DynamoDBProperty]
        public DateTime ApplicationDate { get; set; }

        [DynamoDBProperty]
        public string ProductType { get; set; }

        [DynamoDBProperty]
        public ApplicationStatus Status { get; set; } = new ApplicationStatus();

        [DynamoDBProperty]
        public Customer Applicant { get; set; } = new Customer();
    }
}