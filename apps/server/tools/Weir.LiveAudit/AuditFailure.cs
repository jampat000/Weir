namespace Weir.LiveAudit;

/// <summary>A check of the audit that did not hold.</summary>
internal sealed class AuditFailure(string message) : Exception(message);
