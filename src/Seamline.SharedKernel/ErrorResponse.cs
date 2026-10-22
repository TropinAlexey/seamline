namespace Seamline.SharedKernel;

// The one error body shape the API returns for 400 and 409 — written by the
// composition root's exception middleware and by endpoints that reject a
// request themselves. Named so the OpenAPI document (ADR-0030) describes it.
public sealed record ErrorResponse(string Error);
