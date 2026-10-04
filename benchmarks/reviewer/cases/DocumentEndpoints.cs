using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Shop.Documents;

[ApiController]
[Authorize]
[Route("api/documents")]
public sealed class DocumentEndpoints(IDocumentStore documents) : ControllerBase
{
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<DocumentDto>> Get(Guid id, CancellationToken cancellationToken)
    {
        var document = await documents.FindAsync(id, cancellationToken);
        if (document is null)
        {
            return NotFound();
        }

        return new DocumentDto(document.Id, document.Title, document.Content);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var document = await documents.FindAsync(id, cancellationToken);
        if (document is null)
        {
            return NotFound();
        }

        if (document.OwnerId != User.GetUserId())
        {
            return Forbid();
        }

        await documents.DeleteAsync(id, cancellationToken);
        return NoContent();
    }
}
