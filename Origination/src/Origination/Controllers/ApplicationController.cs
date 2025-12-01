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
            throw new NotImplementedException();
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
            throw new NotImplementedException();
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
            throw new NotImplementedException();
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new ApplicationResponse() { Code = StatusCodes.Status500InternalServerError, Message = "Error while uploading document" });
        }

    }

    [HttpGet("{applicationId}", Name = "ApplicationDetails")]
    public IActionResult Get(Guid applicationId)
    {
        try
        {
            throw new NotImplementedException();
        }
        catch (Exception)
        {
            Response.StatusCode = StatusCodes.Status500InternalServerError;
            return null;
        }
    }


    [HttpGet("{applicationId}/status", Name = "GetApplicationStatus")]
    public int GetStatus(Guid applicationId)
    {
        try
        {
            throw new NotImplementedException();
        }
        catch (Exception)
        {
            Response.StatusCode = StatusCodes.Status500InternalServerError;
            return -500;
        }
    }

    [HttpPost("{applicationId}/status", Name = "UpdateStatus")]
    public IActionResult UpdateApplicationStatus(Guid applicationId, ApplicationStatusUpdateRequest newStatus)
    {
        try
        {
            throw new NotImplementedException();
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new ApplicationResponse() { Code = StatusCodes.Status500InternalServerError, Message = "Error while updating application record" });
        }
    }



}