namespace Seamline.SharedKernel;

// A request that is well-formed but conflicts with the aggregate's current
// state — e.g. delivering a Draft trade. The API maps exactly this type to
// 409 (ADR-0030); a bare InvalidOperationException stays a 500, because
// catching that broadly would also swallow genuine bugs.
public sealed class DomainRuleViolationException(string message) : InvalidOperationException(message);
