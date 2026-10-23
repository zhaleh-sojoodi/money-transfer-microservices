using Microsoft.AspNetCore.Mvc;
using TransferApi.Domain.Requests;
using TransferApi.Services.IServices;

namespace TransferApi.Controllers;

[ApiController]
[Route("transfers")]
public class TransferController : ControllerBase
{
    private readonly ITransferService _transferService;

    public TransferController(ITransferService transferService)
    {
        _transferService = transferService;
    }

    [HttpPost]
    public async Task<IActionResult> CreateTransfer([FromBody] CreateTransferRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _transferService.CreateAsync(request, cancellationToken);
            return CreatedAtAction(nameof(GetTransfer), new { id = result.Id }, result);
        }
        catch (ArgumentException ex)
        {
            ModelState.AddModelError(ex.ParamName ?? string.Empty, ex.Message);
            return ValidationProblem(ModelState);
        }
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetTransfer(Guid id, CancellationToken cancellationToken)
    {
        var result = await _transferService.GetByIdAsync(id, cancellationToken);
        if (result == null)
        {
            return NotFound();
        }

        return Ok(result);
    }
}
