using Origination.Model;

namespace Origination.Service
{
    public interface IApplicationService
    {
        Application GetApplication(Guid applicationId);
        void CreateApplication(Application application);
        void UpdateApplication(Application application);
        void SubmitApplicationFile(Guid applicationId, DocumentType docuType, string fileName, Stream file);
    }
}