using Origination.Model;
namespace Origination.Dto
{
    public class ApplicationStatusUpdateRequest
    {
        public DocumentType DocType { get; set; }
        public ValidationStatus NewStatus { get; set; }
        public string Remarks { get; set; }

    }

    public enum ValidationStatus
    {
        NOTSTARTED = 0,
        VALIDATED = 1,
        ESCALATED = 2,
        FAIL = 10
    }
}
