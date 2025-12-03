using Microsoft.AspNetCore.Mvc;
using Origination.Dto;
using Origination.Model;
using Origination.Service;

namespace Origination.Controllers;

[ApiController]
[Route("[controller]")]
public class ApplicationController : ControllerBase
{
    private readonly ILogger<ApplicationController> _logger;
    private readonly IApplicationService _applicationService;

    public ApplicationController(ILogger<ApplicationController> logger, IApplicationService applicationService)
    {
        _logger = logger;
        _applicationService = applicationService;
    }

    [HttpPost(Name = "Create")]
    public IActionResult CreateApplication(ApplicationCreateRequest basicDetails)
    {
        var basicApplication = new Application()
        {
            ApplicationId = Guid.NewGuid(),
            ApplicationDate = DateTime.Now,
            ProductType = basicDetails.ProductType,
            Applicant = new Customer()
            {
                Id = Guid.NewGuid(),
                FirstName = basicDetails.FirstName,
                LastName = basicDetails.LastName,
                Email = basicDetails.Email
            }
        };
        try
        {
            _applicationService.CreateApplication(basicApplication);
            return StatusCode(StatusCodes.Status201Created, new ApplicationResponse() { Code = StatusCodes.Status201Created, Message = $"Application record created {basicApplication.ApplicationId}" });
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new ApplicationResponse() { Code = StatusCodes.Status500InternalServerError, Message = "Error while creating application record" });
        }
    }


    [HttpPost("{applicationId}/customer", Name = "SubmitCustomerDetails")]
    public IActionResult SubmitCustomerDetails(Guid applicationId, Customer applicant)
    {
        try
        {
            var recordToUpdate = _applicationService.GetApplication(applicationId);
            //immutable fields: Id, FirstName, LastName, Email
            applicant.Id = recordToUpdate.Applicant.Id;
            applicant.FirstName = recordToUpdate.Applicant.FirstName;
            applicant.LastName = recordToUpdate.Applicant.LastName;
            recordToUpdate.Applicant = applicant;
            _applicationService.UpdateApplication(recordToUpdate);
            return StatusCode(StatusCodes.Status200OK, new ApplicationResponse() { Code = StatusCodes.Status200OK, Message = "Application record updated" });
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new ApplicationResponse() { Code = StatusCodes.Status500InternalServerError, Message = "Error while updating application record" });
        }
    }

    [HttpPost("{applicationId}/file", Name = "UploadFile")]
    public IActionResult UploadFile(Guid applicationId, DocumentType docuType, IFormFile file)
    {
        try
        {
            _applicationService.SubmitApplicationFile(applicationId, docuType, file.FileName, file.OpenReadStream());
            return StatusCode(StatusCodes.Status201Created, new ApplicationResponse() { Code = StatusCodes.Status201Created, Message = "Application document submitted" });
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new ApplicationResponse() { Code = StatusCodes.Status500InternalServerError, Message = "Error while uploading document" });
        }

    }

    [HttpGet("{applicationId}", Name = "ApplicationDetails")]
    public Application Get(Guid applicationId)
    {
        try
        {
            return _applicationService.GetApplication(applicationId);
        }
        catch (Exception)
        {
            return null;
        }
    }


    [HttpGet("{applicationId}/status", Name = "GetApplicationStatus")]
    public int GetStatus(Guid applicationId)
    {
        try
        {
            var application = _applicationService.GetApplication(applicationId);
            if (application == null)
            {
                return -500;
            }

            return application.Status.OverallStatus;
        }
        catch (Exception)
        {
            return -500;
        }
    }

    [HttpPost("{applicationId}/status", Name = "UpdateStatus")]
    public IActionResult UpdateApplicationStatus(Guid applicationId, ApplicationStatusUpdateRequest newStatus)
    {
        try
        {
            var recordToUpdate = _applicationService.GetApplication(applicationId);
            switch (newStatus.DocType)
            {
                case DocumentType.INCOMESTATEMENT:
                    recordToUpdate.Status.IncomeRequirement.Status = (int)newStatus.NewStatus;
                    recordToUpdate.Status.IncomeRequirement.Remarks = newStatus.Remarks;
                    break;
                case DocumentType.IDENTITYDOCUMENT:
                    recordToUpdate.Status.IdDocValidity.Status = (int)newStatus.NewStatus;
                    recordToUpdate.Status.IdDocValidity.Remarks = newStatus.Remarks;
                    break;
                case DocumentType.SELFIE:
                    recordToUpdate.Status.Ekyc.Status = (int)newStatus.NewStatus;
                    recordToUpdate.Status.Ekyc.Remarks = newStatus.Remarks;
                    break;
                default:
                    break;
            }

            _applicationService.UpdateApplication(recordToUpdate);
            return StatusCode(StatusCodes.Status200OK, new ApplicationResponse() { Code = StatusCodes.Status200OK, Message = "Application status updated" });
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new ApplicationResponse() { Code = StatusCodes.Status500InternalServerError, Message = "Error while updating application record" });
        }
    }
}